using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Resilience;

public static class SchedulingResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Resilience-generated proxy <typeparamref name="TResilienceProxy"/> as the
    /// <see cref="IJobTypeExecutor"/> for a specific job type, using the fluent
    /// <see cref="ISchedulingBuilder"/> pipeline.
    /// </summary>
    /// <typeparam name="TExecutorInterface">
    /// A custom executor interface that extends <see cref="IJobTypeExecutor"/> and carries
    /// Resilience attributes (<c>[Retry]</c>, <c>[CircuitBreaker]</c>, etc.). The Resilience
    /// source generator emits the proxy from this interface.
    /// </typeparam>
    /// <typeparam name="TResilienceProxy">
    /// The source-generated Resilience proxy class for <typeparamref name="TExecutorInterface"/>.
    /// </typeparam>
    /// <remarks>
    /// Define your executor interface with Resilience attributes, implement it, then wire everything:
    /// <code>
    /// // 1. Define an interface with Resilience attributes:
    /// [Retry(MaxAttempts = 5, BackoffMs = 500)]
    /// [CircuitBreaker(FailureThreshold = 3, DurationMs = 30_000)]
    /// public interface ISendEmailJobExecutor : IJobTypeExecutor { }
    ///
    /// // 2. Implement it:
    /// public sealed class SendEmailJobExecutor : ISendEmailJobExecutor { ... }
    ///
    /// // 3. Register in DI (the Resilience generator emits ISendEmailJobExecutorResilienceProxy):
    /// services
    ///     .AddScheduling()
    ///     .WithResilience&lt;ISendEmailJobExecutor, ISendEmailJobExecutorResilienceProxy&gt;();
    /// </code>
    /// The proxy wraps the inner <typeparamref name="TExecutorInterface"/> implementation and
    /// is resolved as <see cref="IJobTypeExecutor"/> by the scheduling worker.
    /// </remarks>
    public static ISchedulingBuilder WithResilience<TExecutorInterface, TResilienceProxy>(
        this ISchedulingBuilder builder)
        where TExecutorInterface : class, IJobTypeExecutor
        where TResilienceProxy : class, TExecutorInterface
    {
        builder.Services.AddTransient<TResilienceProxy>();
        builder.Services.AddTransient<IJobTypeExecutor>(sp => sp.GetRequiredService<TResilienceProxy>());
        return builder;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension shape on <see cref="IServiceCollection"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithResilience<TInterface, TProxy>() instead. Will be removed in the next major.", DiagnosticId = "ZASCH005")]
    public static IServiceCollection AddSchedulingResilience<TExecutorInterface, TResilienceProxy>(
        this IServiceCollection services)
        where TExecutorInterface : class, IJobTypeExecutor
        where TResilienceProxy : class, TExecutorInterface
    {
        services.AddTransient<TResilienceProxy>();
        services.AddTransient<IJobTypeExecutor>(sp => sp.GetRequiredService<TResilienceProxy>());
        return services;
    }

    /// <summary>
    /// Legacy shim that preserves the v1.x extension name when chained from
    /// <see cref="ISchedulingBuilder"/>. Delegates to
    /// <see cref="WithResilience{TExecutorInterface, TResilienceProxy}"/>.
    /// Will be removed in the next major.
    /// </summary>
    [Obsolete("Use AddScheduling().WithResilience<TInterface, TProxy>() instead. Will be removed in the next major.", DiagnosticId = "ZASCH005")]
    public static ISchedulingBuilder AddSchedulingResilience<TExecutorInterface, TResilienceProxy>(
        this ISchedulingBuilder builder)
        where TExecutorInterface : class, IJobTypeExecutor
        where TResilienceProxy : class, TExecutorInterface
        => builder.WithResilience<TExecutorInterface, TResilienceProxy>();
}
