using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Dashboard.Blazor;

public static class BlazorDashboardExtensions
{
    /// <summary>
    /// Registers <see cref="JobsDashboardClient"/> with the given absolute base URI pointing at the Dashboard API.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiBase">Absolute base URI of the dashboard API (e.g., "https://myapp.com/jobs/api/").</param>
    public static IServiceCollection AddJobsDashboardBlazor(
        this IServiceCollection services,
        Uri apiBase)
    {
        services.AddHttpClient<JobsDashboardClient>(c => c.BaseAddress = apiBase);
        return services;
    }
}
