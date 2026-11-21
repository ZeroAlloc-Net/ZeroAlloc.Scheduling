using System.Data.Async;
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// <see cref="IJobStore"/> backed by ZeroAlloc.ORM. Raw SQL with compile-time
/// parameter binding, no change tracker, and nothing that reflects at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Writes the same columns as the EF Core adapter, with the same status values,
/// but not the same table: this store uses <c>SchedulingJobs</c> with native
/// date types, the EF Core adapter <c>ScheduledJobs</c> with dates as ticks.
/// Moving between the two means draining the queue, not repointing the store.
/// </para>
/// <para>
/// This implements <see cref="IJobStore"/> only. The dashboard's
/// <c>IJobDashboardStore</c> is a distinctly larger surface — summaries, paged
/// status queries, operator requeue and delete — and is deliberately left out
/// rather than stubbed, so a dashboard pointed at this store fails at
/// registration instead of silently reporting nothing.
/// </para>
/// <para>
/// A poll also takes back a job that has been Running for longer than
/// <see cref="SchedulingOptions.ClaimTimeout"/>. The store remembers the claim
/// token each job was claimed under, and an outcome it records afterwards,
/// success, failure or dead-letter, applies only while the row still holds that
/// claim. A run that outlived its claim therefore cannot overwrite the run that
/// took it back. An outcome for a job this instance did not claim is recorded
/// unchecked.
/// </para>
/// </remarks>
public sealed class OrmJobStore : IJobStore
{
    private static readonly TimeSpan DefaultClaimTimeout = new SchedulingOptions().ClaimTimeout;

    private readonly JobRepository _repo;
    private readonly IJobDialectQueries _dialectQueries;
    private readonly IOptionsMonitor<SchedulingOptions>? _options;

    // The claim token each job this instance claimed was claimed under, removed once its outcome
    // write has returned. The store is scoped, as is the worker's batch, so this holds one batch.
    // Created on the first claim, so an idle poll allocates nothing for it.
    private Dictionary<Guid, Guid>? _claims;

    /// <summary>
    /// Creates a store over the application's connection, against SQLite.
    /// </summary>
    /// <param name="connection">The connection the job table lives on.</param>
    /// <remarks>
    /// Kept as its own overload rather than folded into the one below with a
    /// default argument, because a default would delete this signature from the
    /// assembly: code compiled against an earlier version calls it by name and
    /// would fail at runtime without being recompiled.
    /// </remarks>
    public OrmJobStore(IAsyncDbConnection connection)
        : this(connection, OrmSchedulingDialect.Sqlite)
    {
    }

    /// <summary>
    /// Creates a store over the application's connection.
    /// </summary>
    /// <param name="connection">The connection the job table lives on.</param>
    /// <param name="dialect">
    /// Which database this connection talks to. Selects the spelling of the two
    /// bounded queries; everything else the store issues is plain ANSI.
    /// </param>
    public OrmJobStore(IAsyncDbConnection connection, OrmSchedulingDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _repo = new JobRepository(connection);
        _dialectQueries = dialect switch
        {
            OrmSchedulingDialect.SqlServer => new FetchFirstJobQueries(connection),
            _ => new LimitJobQueries(connection),
        };
    }

    /// <summary>
    /// Creates a store over the application's connection that takes back a
    /// Running job after the configured <see cref="SchedulingOptions.ClaimTimeout"/>.
    /// </summary>
    /// <param name="connection">The connection the job table lives on.</param>
    /// <param name="dialect">Which database this connection talks to.</param>
    /// <param name="options">The scheduling options.</param>
    public OrmJobStore(
        IAsyncDbConnection connection,
        OrmSchedulingDialect dialect,
        IOptionsMonitor<SchedulingOptions> options)
        : this(connection, dialect)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
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
        var now = DateTimeOffset.UtcNow;

        var claimed = await _dialectQueries.ClaimPendingAsync(
            (int)JobStatus.Running,
            (int)JobStatus.Pending,
            (int)JobStatus.Failed,
            now,
            StaleBefore(now, _options?.CurrentValue.ClaimTimeout ?? DefaultClaimTimeout),
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
            (_claims ??= new Dictionary<Guid, Guid>(rows.Count))[row.Id] = claimToken;
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

        var completedAt = DateTimeOffset.UtcNow;
        // The token is released only once the method has done its writes: should one throw,
        // the worker's failure write still carries the claim, and finds the row no longer
        // Running when the success had committed.
        var updated = TryGetClaim(id, out var claimToken)
            ? await _repo.MarkCompletedClaimedAsync(
                id.Value, claimToken, (int)JobStatus.Running, (int)JobStatus.Succeeded, completedAt, ct).ConfigureAwait(false)
            : await _repo.MarkCompletedAsync(
                id.Value, (int)JobStatus.Succeeded, completedAt, ct).ConfigureAwait(false);

        // A run that lost its claim leaves the next occurrence to the run that took it back.
        if (updated == 0)
        {
            _claims?.Remove(id.Value);
            return;
        }

        if (nextRunAt.HasValue && cronExpression is not null)
        {
            // Skips the insert when another live run exists, which the unique index
            // would otherwise reject.
            await _dialectQueries.InsertRecurringAsync(
                JobId.New().Value, existing.TypeName, existing.Payload, (int)JobStatus.Pending,
                maxAttempts, nextRunAt.Value, cronExpression, ct).ConfigureAwait(false);
        }
        else if (nextRunAt.HasValue)
        {
            await _repo.InsertAsync(
                JobId.New().Value, existing.TypeName, existing.Payload, (int)JobStatus.Pending,
                maxAttempts, nextRunAt.Value, cronExpression, ct).ConfigureAwait(false);
        }

        _claims?.Remove(id.Value);
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
        if (TryGetClaim(id, out var claimToken))
        {
            await _repo.MarkForRetryClaimedAsync(
                id.Value, claimToken, (int)JobStatus.Running, (int)JobStatus.Failed, attempts, nextRetryAt, ct).ConfigureAwait(false);
            _claims!.Remove(id.Value);
            return;
        }

        await _repo.MarkForRetryAsync(
            id.Value, (int)JobStatus.Failed, attempts, nextRetryAt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (TryGetClaim(id, out var claimToken))
        {
            await _repo.DeadLetterClaimedAsync(
                id.Value, claimToken, (int)JobStatus.Running, (int)JobStatus.DeadLetter, error, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            _claims!.Remove(id.Value);
            return;
        }

        await _repo.DeadLetterAsync(
            id.Value, (int)JobStatus.DeadLetter, error, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Keyed on type name, like the other stores: a recurring job has at most one
    /// live run, one that is Pending, Running or Failed. Without one, a Pending run
    /// is inserted; the unique index from migration 3 makes that atomic, so
    /// instances that start at once seed a single run. With one, a Pending run takes
    /// the given payload, attempts and schedule, and a Running or Failed run is left
    /// to the worker. Completed runs are history and never come back.
    /// </para>
    /// <para>
    /// Without a cron expression there is nothing recurring to key on, and the job is
    /// enqueued as a one-off.
    /// </para>
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

        if (cronExpression is null)
        {
            await EnqueueAsync(typeName, payload, scheduledAt, maxAttempts, cronExpression: null, ct).ConfigureAwait(false);
            return;
        }

        var inserted = await _dialectQueries.InsertRecurringAsync(
            JobId.New().Value, typeName, payload, (int)JobStatus.Pending,
            maxAttempts, scheduledAt, cronExpression, ct).ConfigureAwait(false);

        if (inserted == 0)
        {
            await _repo.UpdatePendingRecurringAsync(
                typeName, payload, scheduledAt, cronExpression, maxAttempts,
                (int)JobStatus.Pending, ct).ConfigureAwait(false);
        }
    }

    private bool TryGetClaim(JobId id, out Guid claimToken)
    {
        if (_claims is not null && _claims.TryGetValue(id.Value, out claimToken))
            return true;

        claimToken = default;
        return false;
    }

    // The start a claim must be at or before to count as expired. A timeout longer than the
    // time since DateTimeOffset.MinValue yields MinValue instead of overflowing.
    private static DateTimeOffset StaleBefore(DateTimeOffset now, TimeSpan claimTimeout)
        => claimTimeout >= now - DateTimeOffset.MinValue ? DateTimeOffset.MinValue : now - claimTimeout;

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
