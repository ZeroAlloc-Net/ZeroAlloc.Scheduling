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
        var candidates = await _db.Jobs
            .Where(j => j.Status == JobStatus.Pending && j.ScheduledAt <= now)
            .Union(_db.Jobs.Where(j => j.Status == JobStatus.Failed && j.ScheduledAt <= now))
            .OrderBy(j => j.ScheduledAt)
            .Take(batchSize)
            .ToListAsync(ct).ConfigureAwait(false);

        var claimed = new List<JobEntry>(candidates.Count);
        foreach (var entity in candidates)
        {
            entity.Status = JobStatus.Running;
            entity.StartedAt = now;
            claimed.Add(new JobEntry
            {
                Id = entity.Id,
                TypeName = entity.TypeName,
                Payload = entity.Payload,
                Status = JobStatus.Running,
                Attempts = entity.Attempts,
                MaxAttempts = entity.MaxAttempts,
                ScheduledAt = entity.ScheduledAt,
                StartedAt = now,
                CompletedAt = entity.CompletedAt,
                NextRunAt = entity.NextRunAt,
                CronExpression = entity.CronExpression,
                Error = entity.Error,
            });
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return claimed;
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
            .AnyAsync(j => j.TypeName == typeName && j.Status == JobStatus.Pending, ct)
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
