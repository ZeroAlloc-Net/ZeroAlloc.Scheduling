using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace ZeroAlloc.Scheduling.Redis;

public static class RedisSchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingRedis(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.TryAddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        services.TryAddSingleton<IJobStore, RedisJobStore>();
        services.TryAddSingleton<IJobDashboardStore>(sp => (IJobDashboardStore)sp.GetRequiredService<IJobStore>());
        return services;
    }
}
