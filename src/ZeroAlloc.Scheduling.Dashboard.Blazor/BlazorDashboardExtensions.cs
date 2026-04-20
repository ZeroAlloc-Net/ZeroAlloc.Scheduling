using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Dashboard.Blazor;

public static class BlazorDashboardExtensions
{
    /// <summary>Registers <see cref="JobsDashboardClient"/> with a base address pointing at the Dashboard API.</summary>
    public static IServiceCollection AddJobsDashboardBlazor(
        this IServiceCollection services,
        string apiBase = "/jobs/api/")
    {
        services.AddHttpClient<JobsDashboardClient>(c => c.BaseAddress = new Uri(apiBase, UriKind.Relative));
        return services;
    }
}
