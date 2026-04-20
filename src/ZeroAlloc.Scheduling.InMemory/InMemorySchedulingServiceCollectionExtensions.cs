using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Scheduling.InMemory;

public static class InMemorySchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingInMemory(this IServiceCollection services)
    {
        services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        return services;
    }
}
