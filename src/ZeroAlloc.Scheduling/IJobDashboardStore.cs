namespace ZeroAlloc.Scheduling;

/// <summary>Extended store interface for dashboard queries. Implement alongside IJobStore in each adapter.</summary>
public interface IJobDashboardStore
{
    Task<JobSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobEntry>> QueryByStatusAsync(JobStatus[] statuses, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<JobEntry>> GetRecurringAsync(CancellationToken ct = default);
    Task RequeueAsync(JobId id, CancellationToken ct = default);
    Task DeleteAsync(JobId id, CancellationToken ct = default);
}
