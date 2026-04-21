using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Scheduling;

/// <summary>Default <see cref="IScheduler"/> — serialises the job and enqueues via <see cref="IJobStore"/>.</summary>
[RequiresUnreferencedCode("DefaultScheduler uses DefaultJobSerializer which requires reflection. Use a source-generated serializer for trimming/AOT.")]
[RequiresDynamicCode("DefaultScheduler uses DefaultJobSerializer which may require runtime code generation. Use a source-generated serializer for AOT.")]
internal sealed class DefaultScheduler : IScheduler
{
    private readonly IJobStore _store;
    private readonly IJobSerializer _serializer;
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly IReadOnlyDictionary<string, IJobTypeExecutor> _executors;

    public DefaultScheduler(IJobStore store, IJobSerializer serializer,
        IOptionsMonitor<SchedulingOptions> options,
        IEnumerable<IJobTypeExecutor> executors)
    {
        _store = store;
        _serializer = serializer;
        _options = options;
        _executors = executors.ToDictionary(e => e.TypeName, StringComparer.Ordinal);
    }

    public ValueTask EnqueueAsync<TJob>(TJob job, CancellationToken ct = default) where TJob : IJob
        => EnqueueAsync(job, TimeSpan.Zero, ct);

    public ValueTask EnqueueAsync<TJob>(TJob job, TimeSpan delay, CancellationToken ct = default) where TJob : IJob
    {
        var typeName = typeof(TJob).FullName!;
        var payload = _serializer.Serialize(job);
        var scheduledAt = DateTimeOffset.UtcNow.Add(delay);
        var maxAttempts = _executors.TryGetValue(typeName, out var executor) && executor.MaxAttempts > 0
            ? executor.MaxAttempts
            : _options.CurrentValue.DefaultMaxAttempts;
        return _store.EnqueueAsync(typeName, payload, scheduledAt, maxAttempts, null, ct);
    }
}
