using Microsoft.EntityFrameworkCore;

namespace ZeroAlloc.Scheduling.EfCore;

public sealed class EfCoreJobStore : IJobStore
{
    private readonly SchedulingDbContext _db;

    public EfCoreJobStore(SchedulingDbContext db) => _db = db;

    public async ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        _db.Jobs.Add(new JobEntryEntity
        {
            Id = Guid.NewGuid(), TypeName = typeName, Payload = payload,
            Status = JobStatus.Pending, Attempts = 0, MaxAttempts = maxAttempts,
            ScheduledAt = scheduledAt, CronExpression = cronExpression,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Step 1: Select candidate IDs only (no tracking, cheap)
        var candidateIds = await _db.Jobs
            .Where(j => (j.Status == JobStatus.Pending || j.Status == JobStatus.Failed)
                     && j.ScheduledAt <= now)
            .OrderBy(j => j.ScheduledAt)
            .Take(batchSize)
            .Select(j => j.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        if (candidateIds.Count == 0)
            return Array.Empty<JobEntry>();

        // Step 2: Conditional atomic claim — only rows still Pending/Failed get marked Running.
        // A second worker racing here finds Status already Running and updates 0 rows.
        await _db.Jobs
            .Where(j => candidateIds.Contains(j.Id)
                     && (j.Status == JobStatus.Pending || j.Status == JobStatus.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, now), ct).ConfigureAwait(false);

        // Step 3: Read back only the rows we actually claimed
        var claimed = await _db.Jobs
            .AsNoTracking()
            .Where(j => candidateIds.Contains(j.Id) && j.Status == JobStatus.Running)
            .ToListAsync(ct).ConfigureAwait(false);

        return claimed.Select(e => e.ToJobEntry()).ToList();
    }

    public async ValueTask MarkSucceededAsync(Guid id, DateTimeOffset? nextRunAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        var entity = await _db.Jobs.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (entity is null) return;

        entity.Status = JobStatus.Succeeded;
        entity.CompletedAt = DateTimeOffset.UtcNow;

        if (nextRunAt.HasValue)
        {
            _db.Jobs.Add(new JobEntryEntity
            {
                Id = Guid.NewGuid(), TypeName = entity.TypeName, Payload = entity.Payload,
                Status = JobStatus.Pending, Attempts = 0, MaxAttempts = maxAttempts,
                ScheduledAt = nextRunAt.Value, CronExpression = cronExpression,
            });
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask MarkFailedAsync(Guid id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        var entity = await _db.Jobs.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (entity is null) return;
        entity.Status = JobStatus.Failed;
        entity.Attempts = attempts;
        entity.ScheduledAt = nextRetryAt;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DeadLetterAsync(Guid id, string error, CancellationToken ct)
    {
        var entity = await _db.Jobs.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (entity is null) return;
        entity.Status = JobStatus.DeadLetter;
        entity.CompletedAt = DateTimeOffset.UtcNow;
        entity.Error = error;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        bool exists = await _db.Jobs
            .AnyAsync(j => j.TypeName == typeName && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            _db.Jobs.Add(new JobEntryEntity
            {
                Id = Guid.NewGuid(), TypeName = typeName, Payload = payload,
                Status = JobStatus.Pending, Attempts = 0, MaxAttempts = maxAttempts,
                ScheduledAt = scheduledAt, CronExpression = cronExpression,
            });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
