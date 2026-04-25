using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Scheduling.InMemory;

public static class InMemorySchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory <see cref="IJobStore"/> against the scheduling builder.
    /// </summary>
    public static ISchedulingBuilder WithInMemoryStore(this ISchedulingBuilder builder)
    {
        builder.Services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        return builder;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension shape on <see cref="IServiceCollection"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithInMemoryStore() instead. Will be removed in the next major.", DiagnosticId = "ZASCH001")]
    public static IServiceCollection AddSchedulingInMemory(this IServiceCollection services)
    {
        services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        return services;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension name when chained from
    /// <see cref="ISchedulingBuilder"/> (post-builder return-type change). Delegates to
    /// <see cref="WithInMemoryStore"/>. Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithInMemoryStore() instead. Will be removed in the next major.", DiagnosticId = "ZASCH001")]
    public static ISchedulingBuilder AddSchedulingInMemory(this ISchedulingBuilder builder)
        => builder.WithInMemoryStore();
}
