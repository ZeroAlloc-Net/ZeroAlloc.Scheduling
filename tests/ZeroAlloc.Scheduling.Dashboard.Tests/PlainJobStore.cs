namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>A store that does not implement <see cref="IJobDashboardStore"/>, so the dashboard answers with empty data.</summary>
internal sealed class PlainJobStore : IJobStore
{
    public ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<JobEntry>>([]);

    public ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        => ValueTask.CompletedTask;
}
