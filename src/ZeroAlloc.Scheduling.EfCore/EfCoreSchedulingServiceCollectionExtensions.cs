using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZeroAlloc.Outbox;

namespace ZeroAlloc.Scheduling.EfCore;

public static class EfCoreSchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers EF Core-backed scheduling services on the given <see cref="ISchedulingBuilder"/>.
    /// </summary>
    public static ISchedulingBuilder WithEfCore(
        this ISchedulingBuilder builder,
        Action<DbContextOptionsBuilder> configure)
    {
        builder.Services.AddDbContext<SchedulingDbContext>(configure);
        builder.Services.TryAddScoped<IJobStore, EfCoreJobStore>();
        builder.Services.TryAddScoped<IJobDashboardStore>(sp => (IJobDashboardStore)sp.GetRequiredService<IJobStore>());
        return builder;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension shape on <see cref="IServiceCollection"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithEfCore(...) instead. Will be removed in the next major.", DiagnosticId = "ZASCH002")]
    public static IServiceCollection AddSchedulingEfCore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<SchedulingDbContext>(configure);
        services.TryAddScoped<IJobStore, EfCoreJobStore>();
        services.TryAddScoped<IJobDashboardStore>(sp => (IJobDashboardStore)sp.GetRequiredService<IJobStore>());
        return services;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension name when chained from
    /// <see cref="ISchedulingBuilder"/>. Delegates to <see cref="WithEfCore"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithEfCore(...) instead. Will be removed in the next major.", DiagnosticId = "ZASCH002")]
    public static ISchedulingBuilder AddSchedulingEfCore(
        this ISchedulingBuilder builder,
        Action<DbContextOptionsBuilder> configure)
        => builder.WithEfCore(configure);

    /// <summary>
    /// Registers <see cref="IOutboxWriter{TJob}"/> so that a job of type
    /// <typeparamref name="TJob"/> can be enqueued into the outbox store within the same
    /// <c>DbTransaction</c> as a business write (Scheduling#17).
    /// </summary>
    /// <remarks>
    /// Requires <see cref="IOutboxStore"/> and <see cref="IOutboxSerializer"/> to be
    /// registered in the DI container (e.g. via <c>AddOutbox()</c> + <c>WithEfCore()</c>
    /// from <c>ZeroAlloc.Outbox</c>).
    /// </remarks>
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("Serialization may require dynamic code generation.")]
    public static ISchedulingBuilder WithOutboxWriter<TJob>(
        this ISchedulingBuilder builder)
        where TJob : notnull
    {
#pragma warning disable IL2026, IL2091
        builder.Services.TryAddScoped<IOutboxWriter<TJob>, OutboxJobWriter<TJob>>();
#pragma warning restore IL2026, IL2091
        return builder;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension shape on <see cref="IServiceCollection"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithOutboxWriter<TJob>() instead. Will be removed in the next major.", DiagnosticId = "ZASCH003")]
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("Serialization may require dynamic code generation.")]
    public static IServiceCollection AddSchedulingOutboxWriter<TJob>(
        this IServiceCollection services)
        where TJob : notnull
    {
#pragma warning disable IL2026, IL2091
        services.TryAddScoped<IOutboxWriter<TJob>, OutboxJobWriter<TJob>>();
#pragma warning restore IL2026, IL2091
        return services;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension name when chained from
    /// <see cref="ISchedulingBuilder"/>. Delegates to <see cref="WithOutboxWriter{TJob}"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithOutboxWriter<TJob>() instead. Will be removed in the next major.", DiagnosticId = "ZASCH003")]
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("Serialization may require dynamic code generation.")]
    public static ISchedulingBuilder AddSchedulingOutboxWriter<TJob>(
        this ISchedulingBuilder builder)
        where TJob : notnull
        => builder.WithOutboxWriter<TJob>();
}
