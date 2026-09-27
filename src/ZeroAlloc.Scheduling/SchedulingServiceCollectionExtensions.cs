using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling;

public static partial class SchedulingServiceCollectionExtensions
{
    internal const string MissingSerializerMessage =
        "ZeroAlloc.Scheduling: no IJobSerializer is configured. Since Scheduling 2.0, AddScheduling() no " +
        "longer falls back to reflection-based JSON; choose a serializer explicitly:\n" +
        "  AOT-safe:   annotate your job types with [ZeroAllocSerializable] and call " +
        "services.AddSerializerDispatcher() from ZeroAlloc.Serialisation.\n" +
        "  Reflection: call services.AddScheduling().WithSystemTextJsonSerializer() " +
        "for System.Text.Json, which is not trim- or AOT-safe.\n" +
        "Or register your own IJobSerializer. " +
        "See https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/migrating-to-v2.md";

    /// <summary>
    /// Registers the scheduling worker, the default <see cref="IScheduler"/> and the job serializer.
    /// Register a store separately, for example with <c>.WithInMemoryStore()</c> or
    /// <c>.WithEfCore(...)</c>, and each job with its generated <c>Add{Job}Job()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is trim- and AOT-safe. It never registers the reflection-based
    /// <see cref="SystemTextJsonJobSerializer"/> on its own. The <see cref="IJobSerializer"/> it
    /// registers resolves to the AOT-safe <see cref="DispatchingJobSerializer"/> when an
    /// <see cref="ISerializerDispatcher"/> is registered, before or after this call, for example with
    /// <c>services.AddSerializerDispatcher()</c> from <c>ZeroAlloc.Serialisation</c>.
    /// </para>
    /// <para>
    /// When there is none, resolving <see cref="IJobSerializer"/> throws an
    /// <see cref="InvalidOperationException"/> that names both options. Every generated job executor
    /// needs the serializer, and the worker builds the executors when the host starts, so a
    /// missing choice fails the host start. Opt in to reflection-based JSON with
    /// <see cref="WithSystemTextJsonSerializer"/>. An <see cref="IJobSerializer"/> the application
    /// registered itself before this call is kept.
    /// </para>
    /// </remarks>
    public static ISchedulingBuilder AddScheduling(
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

        // The dispatcher is looked up when the serializer is first resolved, so
        // AddSerializerDispatcher() may run before or after AddScheduling().
        services.TryAddSingleton<IJobSerializer>(sp =>
        {
            var dispatcher = sp.GetService<ISerializerDispatcher>();
            return dispatcher is not null
                ? new DispatchingJobSerializer(dispatcher)
                : throw new InvalidOperationException(MissingSerializerMessage);
        });

        services.TryAddScoped<IScheduler, DefaultScheduler>();
        services.AddSingleton<SchedulingWorkerService>();
        services.AddHostedService(sp => sp.GetRequiredService<SchedulingWorkerService>());

        return new SchedulingBuilder(services);
    }

    /// <summary>
    /// Serializes job payloads with the reflection-based <see cref="SystemTextJsonJobSerializer"/>.
    /// </summary>
    /// <remarks>
    /// This replaces every <see cref="IJobSerializer"/> registered so far, including the
    /// <see cref="DispatchingJobSerializer"/> default that <c>AddScheduling()</c> selects when an
    /// <see cref="ISerializerDispatcher"/> is registered. It writes the same format as the 1.x
    /// fallback, so jobs already in a store stay readable. It is not trim- or AOT-safe; for
    /// NativeAOT, call <c>services.AddSerializerDispatcher()</c> instead and leave this out.
    /// </remarks>
    [RequiresUnreferencedCode("WithSystemTextJsonSerializer registers SystemTextJsonJobSerializer, which uses reflection-based System.Text.Json and may not work after trimming. For trim- and AOT-safe serialisation, call services.AddSerializerDispatcher() instead.")]
    [RequiresDynamicCode("WithSystemTextJsonSerializer registers SystemTextJsonJobSerializer, which uses reflection-based System.Text.Json and may need runtime code generation. For AOT-safe serialisation, call services.AddSerializerDispatcher() instead.")]
    public static ISchedulingBuilder WithSystemTextJsonSerializer(this ISchedulingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.RemoveAll<IJobSerializer>();
        builder.Services.AddSingleton<IJobSerializer>(new SystemTextJsonJobSerializer());
        return builder;
    }
}
