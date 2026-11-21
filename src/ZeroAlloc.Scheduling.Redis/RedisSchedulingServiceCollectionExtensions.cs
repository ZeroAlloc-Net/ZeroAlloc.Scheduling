using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ZeroAlloc.Scheduling.Redis;

public static class RedisSchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Redis <see cref="IJobStore"/> against the scheduling builder.
    /// </summary>
    public static ISchedulingBuilder WithRedis(
        this ISchedulingBuilder builder,
        string connectionString)
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        builder.Services.TryAddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        builder.Services.TryAddSingleton<IJobStore>(sp => new RedisJobStore(
            sp.GetRequiredService<IDatabase>(),
            sp.GetRequiredService<IOptionsMonitor<SchedulingOptions>>()));
        builder.Services.TryAddSingleton<IJobDashboardStore>(sp => (IJobDashboardStore)sp.GetRequiredService<IJobStore>());
        return builder;
    }
}
