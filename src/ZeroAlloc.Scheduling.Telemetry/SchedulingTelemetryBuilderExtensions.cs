using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.Telemetry;

/// <summary>
/// Fluent extensions on <see cref="ISchedulingBuilder"/> that wire ZeroAlloc.Telemetry
/// instrumentation into every registered <see cref="IJobTypeExecutor"/>.
/// </summary>
public static class SchedulingTelemetryBuilderExtensions
{
    /// <summary>
    /// Decorates every registered <see cref="IJobTypeExecutor"/> with a source-generated
    /// OpenTelemetry proxy. Spans, counters, and histograms are emitted on
    /// <see cref="IJobTypeExecutor.ExecuteAsync"/>. ActivitySource: "ZeroAlloc.Scheduling".
    /// Safe to call multiple times — already-decorated executors are skipped.
    /// </summary>
    public static ISchedulingBuilder WithTelemetry(this ISchedulingBuilder builder)
    {
        var services = builder.Services;
        // Idempotence sentinel — once we've decorated, don't decorate again.
        if (services.Any(d => d.ServiceType == typeof(SchedulingTelemetryMarker))) return builder;
        services.AddSingleton<SchedulingTelemetryMarker>();

        for (int i = 0; i < services.Count; i++)
        {
            var d = services[i];
            if (d.ServiceType != typeof(IJobTypeExecutor)) continue;

            var captured = d;
            services[i] = ServiceDescriptor.Describe(
                typeof(IJobTypeExecutor),
                sp =>
                {
                    var inner = (IJobTypeExecutor)CreateFromDescriptor(captured, sp);
                    return new JobExecutorTelemetryAdapter(inner);
                },
                captured.Lifetime);
        }
        return builder;
    }

    private static object CreateFromDescriptor(ServiceDescriptor d, IServiceProvider sp)
    {
        if (d.ImplementationInstance is not null) return d.ImplementationInstance;
        if (d.ImplementationFactory is not null) return d.ImplementationFactory(sp);
        return ActivatorUtilities.CreateInstance(sp, d.ImplementationType!);
    }

    /// <summary>Marker singleton used to make <see cref="WithTelemetry"/> idempotent.</summary>
    private sealed class SchedulingTelemetryMarker { }
}
