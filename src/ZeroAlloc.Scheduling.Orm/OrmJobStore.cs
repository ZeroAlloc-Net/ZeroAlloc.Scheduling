using System.Data.Async;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// <see cref="IJobStore"/> backed by ZeroAlloc.ORM. Raw SQL with compile-time
/// parameter binding, no change tracker, and nothing that reflects at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Writes the same columns as the EF Core adapter, with the same status values,
/// so the two can be swapped without a data migration.
/// </para>
/// <para>
/// This implements <see cref="IJobStore"/> only. The dashboard's
/// <c>IJobDashboardStore</c> is a distinctly larger surface — summaries, paged
/// status queries, operator requeue and delete — and is deliberately left out
/// rather than stubbed, so a dashboard pointed at this store fails at
/// registration instead of silently reporting nothing.
/// </para>
/// </remarks>
public sealed class OrmJobStore : IJobStore
{
    private readonly JobRepository _repo;
    private readonly IJobDialectQueries _dialectQueries;

    /// <summary>
    /// Creates a store over the application's connection.
    /// </summary>
    /// <param name="connection">The connection the job table lives on.</param>
    /// <param name="dialect">
    /// Which database this connection talks to. Selects the spelling of the two
    /// bounded queries; everything else the store issues is plain ANSI. Defaults
    /// to SQLite, which shares its spelling with PostgreSQL.
    /// </param>
    public OrmJobStore(IAsyncDbConnection connection, OrmSchedulingDialect dialect = OrmSchedulingDialect.Sqlite)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _repo = new JobRepository(connection);
        _dialectQueries = dialect switch
        {
            OrmSchedulingDialect.SqlServer => new FetchFirstJobQueries(connection),
            _ => new LimitJobQueries(connection),
        };
    }

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(
        string typeName,
        byte[] payload,
        DateTimeOffset scheduledAt,
        int maxAttempts,
        string? cronExpression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(payload);

        await _repo.InsertAsync(
            JobId.New().Value, typeName, payload, (int)JobStatus.Pending,
            maxAttempts, scheduledAt, cronExpression, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Claims under a token this call generates, then reads back by that token,
    /// so the rows returned are exactly the rows this call claimed even when
    /// several pollers run concurrently. See
    /// <see cref="JobRepository.ReadClaimedAsync"/> for why that matters.
    /// </remarks>
    public async ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize <= 0) return [];

        var claimToken = Guid.NewGuid();

        var claimed = await _dialectQueries.ClaimPendingAsync(
            (int)JobStatus.Running,
            (int)JobStatus.Pending,
            (int)JobStatus.Failed,
            DateTimeOffset.UtcNow,
            batchSize,
            claimToken,
            ct).ConfigureAwait(false);

        // Skip the read-back when the UPDATE took nothing, which is the common
        // case for an idle scheduler polling on a timer.
        if (claimed == 0) return [];

        var rows = await _repo.ReadClaimedAsync(claimToken, ct).ConfigureAwait(false);

        var result = new List<JobEntry>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(ToEntry(row));
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A recurring job whose next occurrence is known is re-enqueued as a fresh
    /// Pending row rather than rewound in place, matching the EF Core adapter:
    /// the completed run stays on record and the next one gets its own identity.
    /// </remarks>
    public async ValueTask MarkSucceededAsync(
        JobId id,
        DateTimeOffset? nextRunAt,
        string? cronExpression,
        int maxAttempts,
        CancellationToken ct)
    {
        var existing = await _repo.GetAsync(id.Value, ct).ConfigureAwait(false);
        if (existing is null) return;

        await _repo.MarkCompletedAsync(
            id.Value, (int)JobStatus.Succeeded, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

        if (nextRunAt.HasValue)
        {
            await _repo.InsertAsync(
                JobId.New().Value, existing.TypeName, existing.Payload, (int)JobStatus.Pending,
                maxAttempts, nextRunAt.Value, cronExpression, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Goes back to Failed rather than staying Running, and moves ScheduledAt to
    /// the retry time. The claim query treats Failed as claimable, so the job is
    /// picked up again once that time passes.
    /// </remarks>
    public async ValueTask MarkFailedAsync(
        JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        await _repo.MarkForRetryAsync(
            id.Value, (int)JobStatus.Failed, attempts, nextRetryAt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(error);

        await _repo.DeadLetterAsync(
            id.Value, (int)JobStatus.DeadLetter, error, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Keyed on type name, matching the EF Core adapter: one recurring
    /// definition per job type. Re-registering the same type updates the
    /// existing row rather than accumulating duplicates that would each fire.
    /// </remarks>
    public async ValueTask UpsertRecurringAsync(
        string typeName,
        byte[] payload,
        DateTimeOffset scheduledAt,
        string? cronExpression,
        int maxAttempts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(payload);

        var existing = await _dialectQueries.FindRecurringAsync(typeName, ct).ConfigureAwait(false);

        if (existing is null)
        {
            await _repo.InsertAsync(
                JobId.New().Value, typeName, payload, (int)JobStatus.Pending,
                maxAttempts, scheduledAt, cronExpression, ct).ConfigureAwait(false);
            return;
        }

        await _repo.UpdateRecurringAsync(
            existing.Id, payload, scheduledAt, cronExpression, maxAttempts,
            (int)JobStatus.Pending, ct).ConfigureAwait(false);
    }

    private static JobEntry ToEntry(JobRow row) => new()
    {
        Id = new JobId(row.Id),
        TypeName = row.TypeName,
        Payload = row.Payload,
        Status = (JobStatus)row.Status,
        Attempts = row.Attempts,
        MaxAttempts = row.MaxAttempts,
        ScheduledAt = row.ScheduledAt,
        StartedAt = row.StartedAt,
        CompletedAt = row.CompletedAt,
        NextRunAt = row.NextRunAt,
        CronExpression = row.CronExpression,
        Error = row.Error,
    };
}
