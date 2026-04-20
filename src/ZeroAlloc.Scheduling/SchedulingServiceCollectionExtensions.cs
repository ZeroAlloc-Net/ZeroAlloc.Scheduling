using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZeroAlloc.Scheduling;

public static partial class SchedulingServiceCollectionExtensions
{
    [RequiresUnreferencedCode("AddScheduling registers DefaultJobSerializer which uses reflection-based JSON. Use a source-generated serializer for trimming/AOT.")]
    [RequiresDynamicCode("AddScheduling registers DefaultJobSerializer which may require runtime code generation. Use a source-generated serializer for AOT.")]
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

        services.TryAddSingleton<IJobSerializer, DefaultJobSerializer>();
        services.TryAddScoped<IScheduler, DefaultScheduler>();
        services.AddSingleton<SchedulingWorkerService>();
        services.AddHostedService(sp => sp.GetRequiredService<SchedulingWorkerService>());

        return services;
    }
}
