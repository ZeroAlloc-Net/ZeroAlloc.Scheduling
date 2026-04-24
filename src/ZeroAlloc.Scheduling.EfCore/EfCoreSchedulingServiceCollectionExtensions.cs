using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZeroAlloc.Outbox;

namespace ZeroAlloc.Scheduling.EfCore;

public static class EfCoreSchedulingServiceCollectionExtensions
{
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
    /// Registers <see cref="IOutboxWriter{TJob}"/> so that a job of type
    /// <typeparamref name="TJob"/> can be enqueued into the outbox store within the same
    /// <c>DbTransaction</c> as a business write (Scheduling#17).
    /// </summary>
    /// <remarks>
    /// Requires <see cref="IOutboxStore"/> and <see cref="IOutboxSerializer"/> to be
    /// registered in the DI container (e.g. via <c>AddOutbox()</c> + <c>AddOutboxEfCore()</c>
    /// from <c>ZeroAlloc.Outbox</c>).
    /// </remarks>
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
}
