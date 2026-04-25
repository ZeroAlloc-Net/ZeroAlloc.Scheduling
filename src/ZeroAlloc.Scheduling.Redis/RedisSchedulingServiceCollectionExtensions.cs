using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        builder.Services.TryAddSingleton<IJobStore, RedisJobStore>();
        builder.Services.TryAddSingleton<IJobDashboardStore>(sp => (IJobDashboardStore)sp.GetRequiredService<IJobStore>());
        return builder;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension shape on <see cref="IServiceCollection"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithRedis(connectionString) instead. Will be removed in the next major.", DiagnosticId = "ZASCH006")]
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

    /// <summary>
    /// Legacy shim that preserves the v1.x extension name when chained from
    /// <see cref="ISchedulingBuilder"/> (post-builder return-type change). Delegates to
    /// <see cref="WithRedis"/>. Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithRedis(connectionString) instead. Will be removed in the next major.", DiagnosticId = "ZASCH006")]
    public static ISchedulingBuilder AddSchedulingRedis(
        this ISchedulingBuilder builder,
        string connectionString)
        => builder.WithRedis(connectionString);
}
