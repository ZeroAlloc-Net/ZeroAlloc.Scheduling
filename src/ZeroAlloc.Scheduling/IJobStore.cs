namespace ZeroAlloc.Scheduling;

/// <summary>Durable backing store for job entries.</summary>
public interface IJobStore
{
    /// <summary>Persists a new pending job.</summary>
    ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct);

    /// <summary>Atomically fetches up to <paramref name="batchSize"/> jobs where Status=Pending and ScheduledAt &lt;= UtcNow, marking them Running.</summary>
    ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct);

    /// <summary>Marks a job succeeded. For recurring jobs, inserts a new Pending entry with the next scheduled time.</summary>
    ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct);

    /// <summary>Records a failed attempt and reschedules the retry.</summary>
    ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct);

    /// <summary>Moves a job to dead-letter after exhausting all attempts.</summary>
    ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct);

    /// <summary>Upserts a recurring job's initial Pending entry on startup (keyed by TypeName — no-op if one already exists).</summary>
    ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct);
}
