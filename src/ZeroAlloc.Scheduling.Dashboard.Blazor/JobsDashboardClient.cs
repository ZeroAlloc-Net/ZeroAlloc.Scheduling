using System.Net.Http.Json;

namespace ZeroAlloc.Scheduling.Dashboard.Blazor;

/// <summary>HTTP client wrapper for the Dashboard Minimal API.</summary>
public sealed class JobsDashboardClient
{
    private readonly HttpClient _http;

    public JobsDashboardClient(HttpClient http) => _http = http;

    public async Task<JobSummary?> GetSummaryAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("summary", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobSummary>(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobEntry>?> GetPendingAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("pending", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<JobEntry>>(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobEntry>?> GetRunningAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("running", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<JobEntry>>(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobEntry>?> GetFailedAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("failed", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<JobEntry>>(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobEntry>?> GetSucceededAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("succeeded", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<JobEntry>>(ct).ConfigureAwait(false);
    }

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
