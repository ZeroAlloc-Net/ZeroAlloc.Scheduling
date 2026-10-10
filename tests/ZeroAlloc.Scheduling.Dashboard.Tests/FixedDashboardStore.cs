namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>A dashboard store that always answers with <see cref="SampleJobs"/>.</summary>
internal sealed class FixedDashboardStore : IJobStore, IJobDashboardStore
{
    public Task<JobSummary> GetSummaryAsync(CancellationToken ct = default) => Task.FromResult(new JobSummary(1, 2, 3, 4, 5));

    public Task<IReadOnlyList<JobEntry>> QueryByStatusAsync(JobStatus[] statuses, int page = 1, int pageSize = 50, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobEntry>>(SampleJobs.All.Where(e => statuses.Contains(e.Status)).ToList());

    public Task<IReadOnlyList<JobEntry>> GetRecurringAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobEntry>>(SampleJobs.All.Where(e => e.CronExpression is not null).ToList());

    public Task RequeueAsync(JobId id, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(JobId id, CancellationToken ct = default) => Task.CompletedTask;

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
