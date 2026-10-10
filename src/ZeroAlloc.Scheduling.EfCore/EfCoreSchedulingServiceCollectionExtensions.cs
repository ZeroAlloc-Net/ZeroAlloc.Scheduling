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
    [RequiresUnreferencedCode("EF Core builds its model with reflection and is not trim-safe; see https://aka.ms/efcore-docs-trimming. Use it only from applications that are not trimmed or published with NativeAOT.")]
    [RequiresDynamicCode("EF Core generates code at runtime and is not NativeAOT-compatible; see https://aka.ms/efcore-docs-trimming. Use it only from applications that are not published with NativeAOT.")]
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
    /// Registers <see cref="IOutboxWriter{TJob}"/> so that a job of type
    /// <typeparamref name="TJob"/> can be enqueued into the outbox store within the same
    /// <c>DbTransaction</c> as a business write (Scheduling#17).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires <see cref="IOutboxStore"/> and <see cref="IOutboxSerializer"/> in the DI container.
    /// Register the store with <c>AddOutbox()</c> and a store adapter such as
    /// <c>WithEfCore&lt;TContext&gt;()</c>. Since ZeroAlloc.Outbox 4.0, <c>AddOutbox()</c> no longer
    /// falls back to a serializer, so choose one explicitly:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>services.AddSerializerDispatcher()</c> from <c>ZeroAlloc.Serialisation</c>: trim- and
    ///   AOT-safe. Annotate <typeparamref name="TJob"/> with <c>[ZeroAllocSerializable]</c>.</item>
    ///   <item><c>.WithSystemTextJsonSerializer()</c> on the outbox builder: reflection-based
    ///   System.Text.Json, which warns at that call under trimming or NativeAOT.</item>
    /// </list>
    /// <para>
    /// Without either, resolving <see cref="IOutboxWriter{TJob}"/> throws an
    /// <see cref="InvalidOperationException"/> that names both options.
    /// </para>
    /// </remarks>
    public static ISchedulingBuilder WithOutboxWriter<TJob>(
        this ISchedulingBuilder builder)
        where TJob : notnull
    {
        builder.Services.TryAddScoped<IOutboxWriter<TJob>, OutboxJobWriter<TJob>>();
        return builder;
    }
}
