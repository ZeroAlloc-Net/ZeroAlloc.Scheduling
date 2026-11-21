using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.AotSmoke;

/// <summary>
/// An in-memory store that loses the first recurring seed, as a failed write would, so only a later
/// seed can schedule the job.
/// </summary>
internal sealed class FirstSeedLosingStore(InMemoryJobStore inner) : IJobStore
{
    private int _seeds;

    public int Seeds => Volatile.Read(ref _seeds);

    public ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct)
        => inner.EnqueueAsync(typeName, payload, scheduledAt, maxAttempts, cronExpression, ct);

    public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
        => inner.FetchPendingAsync(batchSize, ct);

    public ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        => inner.MarkSucceededAsync(id, nextRunAt, cronExpression, maxAttempts, ct);

    public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
        => inner.MarkFailedAsync(id, attempts, nextRetryAt, ct);

    public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
        => inner.DeadLetterAsync(id, error, ct);

    public ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        => Interlocked.Increment(ref _seeds) == 1
            ? ValueTask.CompletedTask
            : inner.UpsertRecurringAsync(typeName, payload, scheduledAt, cronExpression, maxAttempts, ct);
}
