using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Mediator;

public static class MediatorSchedulingExtensions
{
    /// <summary>
    /// Marker entrypoint for the ZeroAlloc.Scheduling mediator bridge on
    /// <see cref="ISchedulingBuilder"/>.
    /// <para>
    /// Job types decorated with <c>[Job]</c> that also implement <c>IRequest&lt;Unit&gt;</c>
    /// have their <see cref="MediatorJobTypeExecutor{TJob}"/> registered automatically
    /// by the source generator via the generated <c>AddXxxJob()</c> extension method.
    /// </para>
    /// <para>
    /// This method is now a no-op retained for source compatibility. It may be safely
    /// removed from application startup code.
    /// </para>
    /// </summary>
    public static ISchedulingBuilder WithMediator(this ISchedulingBuilder builder)
    {
        return builder;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension shape on <see cref="IServiceCollection"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithMediator() instead. Will be removed in the next major.", DiagnosticId = "ZASCH004")]
    public static IServiceCollection AddSchedulingMediator(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension name when chained from
    /// <see cref="ISchedulingBuilder"/>. Delegates to <see cref="WithMediator"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithMediator() instead. Will be removed in the next major.", DiagnosticId = "ZASCH004")]
    public static ISchedulingBuilder AddSchedulingMediator(this ISchedulingBuilder builder)
        => builder.WithMediator();
}
