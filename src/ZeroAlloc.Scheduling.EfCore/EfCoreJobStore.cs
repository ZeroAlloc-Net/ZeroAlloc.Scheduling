using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Scheduling.EfCore;

/// <summary>
/// <see cref="IJobStore"/> and <see cref="IJobDashboardStore"/> backed by EF Core.
/// </summary>
/// <remarks>
/// A poll also takes back a job that has been Running for longer than
/// <see cref="SchedulingOptions.ClaimTimeout"/>. The store remembers the claim token each job was
/// claimed under, and an outcome it records afterwards, success, failure or dead-letter, applies only
/// while the row still holds that claim. A run that outlived its claim therefore cannot overwrite the
/// run that took it back. An outcome for a job this instance did not claim is recorded unchecked.
/// </remarks>
public sealed class EfCoreJobStore : IJobStore, IJobDashboardStore
{
    private static readonly TimeSpan DefaultClaimTimeout = new SchedulingOptions().ClaimTimeout;

    private readonly SchedulingDbContext _db;
    private readonly IOptionsMonitor<SchedulingOptions>? _options;

    // The claim token each job this instance claimed was claimed under, removed again when its
    // outcome is recorded. The store is scoped, as is the worker's batch, so this holds one batch.
    private readonly Dictionary<JobId, Guid> _claims = [];

    /// <summary>
    /// Creates a store that takes back a Running job after the default
    /// <see cref="SchedulingOptions.ClaimTimeout"/>.
    /// </summary>
    /// <param name="db">The context the job table lives in.</param>
    public EfCoreJobStore(SchedulingDbContext db) => _db = db;

    /// <summary>
    /// Creates a store that takes back a Running job after the configured
    /// <see cref="SchedulingOptions.ClaimTimeout"/>.
    /// </summary>
    /// <param name="db">The context the job table lives in.</param>
    /// <param name="options">The scheduling options.</param>
    public EfCoreJobStore(SchedulingDbContext db, IOptionsMonitor<SchedulingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _options = options;
    }

    public async ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        _db.Jobs.Add(new JobEntryEntity
        {
            Id = JobId.New(), TypeName = typeName, Payload = payload,
            Status = JobStatus.Pending, Attempts = 0, MaxAttempts = maxAttempts,
            ScheduledAt = scheduledAt, CronExpression = cronExpression,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // A Running row older than this was abandoned by a run that recorded no outcome.
        var staleBefore = now - (_options?.CurrentValue.ClaimTimeout ?? DefaultClaimTimeout);

        // Identifies this poll's claim. The read-back below filters on it, so it
        // returns exactly the rows this call claimed and nothing a competing
        // poller claimed.
        var claimToken = Guid.NewGuid();

        // Step 1: Select candidate IDs only (no tracking, cheap)
        var candidateIds = await _db.Jobs
            .Where(j => ((j.Status == JobStatus.Pending || j.Status == JobStatus.Failed)
                         && j.ScheduledAt <= now)
                     || (j.Status == JobStatus.Running && j.StartedAt <= staleBefore))
            .OrderBy(j => j.ScheduledAt)
            .Take(batchSize)
            .Select(j => j.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        if (candidateIds.Count == 0)
            return Array.Empty<JobEntry>();

        // Step 2: Conditional atomic claim — only rows still Pending/Failed, or still Running
        // under an expired claim, get marked Running, and each gets this poll's token. A
        // second worker racing here finds Status already Running with a fresh StartedAt and
        // updates 0 rows. Taking back an expired claim counts as a new attempt.
        await _db.Jobs
            .Where(j => candidateIds.Contains(j.Id)
                     && (j.Status == JobStatus.Pending || j.Status == JobStatus.Failed
                         || (j.Status == JobStatus.Running && j.StartedAt <= staleBefore)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Attempts, j => j.Status == JobStatus.Running ? j.Attempts + 1 : j.Attempts)
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, now)
                .SetProperty(j => j.ClaimToken, claimToken), ct).ConfigureAwait(false);

        // Step 3: Read back this poll's claim, by token.
        //
        // Filtering on "candidate and now Running" instead, as this did
        // previously, cannot distinguish our claim from anyone else's: two
        // pollers whose candidate sets overlap both matched every Running row in
        // that set, so the loser of step 2 still returned jobs it had not
        // claimed. Concurrently, with both selecting before either updated, that
        // meant both received the full batch and every job ran twice.
        var claimed = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.ClaimToken == claimToken)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var row in claimed)
            _claims[row.Id] = claimToken;

        return claimed.Select(e => e.ToJobEntry()).ToList();
    }

    public async ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        // The successor is inserted only when the completion applied, and in the same
        // transaction, so a run that lost its claim adds no second successor.
        var ownTransaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        try
        {
            var completedAt = DateTimeOffset.UtcNow;
            var updated = await OwnedRow(id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Succeeded)
                    .SetProperty(j => j.CompletedAt, completedAt), ct).ConfigureAwait(false);

            if (updated > 0 && nextRunAt.HasValue)
            {
                var source = await _db.Jobs
                    .AsNoTracking()
                    .Where(j => j.Id == id)
                    .Select(j => new { j.TypeName, j.Payload })
                    .SingleAsync(ct).ConfigureAwait(false);

                _db.Jobs.Add(new JobEntryEntity
                {
                    Id = JobId.New(), TypeName = source.TypeName, Payload = source.Payload,
                    Status = JobStatus.Pending, Attempts = 0, MaxAttempts = maxAttempts,
                    ScheduledAt = nextRunAt.Value, CronExpression = cronExpression,
                });
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            if (ownTransaction is not null)
                await ownTransaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (ownTransaction is not null)
                await ownTransaction.DisposeAsync().ConfigureAwait(false);
        }

        _claims.Remove(id);
        await RefreshTrackedAsync(id, ct).ConfigureAwait(false);
    }

    public async ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        await OwnedRow(id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.Attempts, attempts)
                .SetProperty(j => j.ScheduledAt, nextRetryAt), ct).ConfigureAwait(false);
        _claims.Remove(id);
        await RefreshTrackedAsync(id, ct).ConfigureAwait(false);
    }

    public async ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        var completedAt = DateTimeOffset.UtcNow;
        await OwnedRow(id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.DeadLetter)
                .SetProperty(j => j.CompletedAt, completedAt)
                .SetProperty(j => j.Error, error), ct).ConfigureAwait(false);
        _claims.Remove(id);
        await RefreshTrackedAsync(id, ct).ConfigureAwait(false);
    }

    // The outcome writes go straight to the database, past the change tracker. When the context
    // also tracks the job, for instance because the same context enqueued it, reload that entity so
    // it shows the outcome instead of its stale state.
    private async Task RefreshTrackedAsync(JobId id, CancellationToken ct)
    {
        var tracked = _db.Jobs.Local.FindEntry(id);
        if (tracked is not null)
            await tracked.ReloadAsync(ct).ConfigureAwait(false);
    }

    // The row an outcome may be written to: the job, and only while it still holds the claim this
    // instance took, so a run that lost its claim to a later poll writes nothing.
    private IQueryable<JobEntryEntity> OwnedRow(JobId id)
    {
        if (!_claims.TryGetValue(id, out var claimToken))
            return _db.Jobs.Where(j => j.Id == id);

        return _db.Jobs.Where(j => j.Id == id && j.ClaimToken == claimToken);
    }

    public async ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        // A live run is one that is Pending, Running or Failed; a Failed run is retried. Finished
        // runs, Succeeded or DeadLetter, are history: the new run is added next to them.
        bool exists = await _db.Jobs
            .AnyAsync(j => j.TypeName == typeName && j.CronExpression != null
                        && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running || j.Status == JobStatus.Failed), ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            _db.Jobs.Add(new JobEntryEntity
            {
                Id = JobId.New(), TypeName = typeName, Payload = payload,
                Status = JobStatus.Pending, Attempts = 0, MaxAttempts = maxAttempts,
                ScheduledAt = scheduledAt, CronExpression = cronExpression,
            });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<JobSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var counts = await _db.Jobs
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        int Get(JobStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        return new JobSummary(
            Get(JobStatus.Pending), Get(JobStatus.Running),
            Get(JobStatus.Succeeded), Get(JobStatus.Failed), Get(JobStatus.DeadLetter));
    }

    public async Task<IReadOnlyList<JobEntry>> QueryByStatusAsync(
        JobStatus[] statuses, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var results = await _db.Jobs
            .AsNoTracking()
            .Where(j => statuses.Contains(j.Status))
            .OrderByDescending(j => j.ScheduledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return results.Select(e => e.ToJobEntry()).ToList();
    }

    public async Task<IReadOnlyList<JobEntry>> GetRecurringAsync(CancellationToken ct = default)
    {
        var results = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.CronExpression != null)
            .OrderBy(j => j.TypeName)
            .ToListAsync(ct).ConfigureAwait(false);
        return results.Select(e => e.ToJobEntry()).ToList();
    }

    public async Task RequeueAsync(JobId id, CancellationToken ct = default)
    {
        await _db.Jobs
            .Where(j => j.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Pending)
                .SetProperty(j => j.Error, (string?)null)
                .SetProperty(j => j.ScheduledAt, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(JobId id, CancellationToken ct = default)
    {
        await _db.Jobs
            .Where(j => j.Id == id)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
