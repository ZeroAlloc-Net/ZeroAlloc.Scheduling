using System.Net.Http.Json;

namespace ZeroAlloc.Scheduling.Dashboard.Blazor;

/// <summary>HTTP client wrapper for the Dashboard Minimal API.</summary>
public sealed class JobsDashboardClient
{
    private readonly HttpClient _http;

    public JobsDashboardClient(HttpClient http) => _http = http;

    public Task<JobSummary?> GetSummaryAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<JobSummary>("summary", ct);

    public Task<IReadOnlyList<JobEntry>?> GetPendingAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<IReadOnlyList<JobEntry>>("pending", ct);

    public Task<IReadOnlyList<JobEntry>?> GetRunningAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<IReadOnlyList<JobEntry>>("running", ct);

    public Task<IReadOnlyList<JobEntry>?> GetFailedAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<IReadOnlyList<JobEntry>>("failed", ct);

    public Task<IReadOnlyList<JobEntry>?> GetSucceededAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<IReadOnlyList<JobEntry>>("succeeded", ct);

    public async Task RequeueAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync($"{id}/requeue", null, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"{id}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
