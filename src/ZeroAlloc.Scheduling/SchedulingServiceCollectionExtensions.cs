using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling;

public static partial class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers ZeroAlloc.Scheduling services. When an <see cref="ISerializerDispatcher"/>
    /// has already been registered (e.g. via <c>services.AddSerializerDispatcher()</c> from
    /// <c>ZeroAlloc.Serialisation</c>), the AOT-safe <see cref="DispatchingJobSerializer"/>
    /// is used automatically. Otherwise the reflection-based <see cref="DefaultJobSerializer"/>
    /// is registered as a fallback.
    /// </summary>
    /// <remarks>
    /// For full AOT safety, annotate your job types with <c>[ZeroAllocSerializable]</c>,
    /// call <c>services.AddSerializerDispatcher()</c> before this method, and suppress the
    /// <c>IL2026</c>/<c>IL3050</c> warnings on the <c>AddScheduling</c> call site.
    /// </remarks>
    [RequiresUnreferencedCode("AddScheduling may register DefaultJobSerializer which uses reflection-based JSON. Call services.AddSerializerDispatcher() first for AOT-safe serialisation.")]
    [RequiresDynamicCode("AddScheduling may register DefaultJobSerializer which may require runtime code generation. Call services.AddSerializerDispatcher() first for AOT-safe serialisation.")]
    public static IServiceCollection AddScheduling(
        this IServiceCollection services,
        Action<SchedulingOptions>? configure = null)
    {
        if (configure != null)
            services.Configure(configure);

        // Ensure a logger factory is registered so SchedulingWorkerService can be resolved
        // even when the host doesn't call AddLogging(). A real host (e.g. WebApplication.CreateBuilder)
        // always registers one; NullLoggerFactory is the safe fallback for tests / minimal containers.
        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Scheduling#15: if the caller has already registered an ISerializerDispatcher
        // (via services.AddSerializerDispatcher() from ZeroAlloc.Serialisation), prefer
        // the AOT-safe DispatchingJobSerializer. Fall back to DefaultJobSerializer only
        // when no dispatcher is present.
        services.TryAddSingleton<IJobSerializer>(sp =>
        {
            var dispatcher = sp.GetService<ISerializerDispatcher>();
            if (dispatcher != null)
                return new DispatchingJobSerializer(dispatcher);

#pragma warning disable IL2026, IL3050
            return new DefaultJobSerializer();
#pragma warning restore IL2026, IL3050
        });

        services.TryAddScoped<IScheduler, DefaultScheduler>();
        services.AddSingleton<SchedulingWorkerService>();
        services.AddHostedService(sp => sp.GetRequiredService<SchedulingWorkerService>());

        return services;
    }
}
