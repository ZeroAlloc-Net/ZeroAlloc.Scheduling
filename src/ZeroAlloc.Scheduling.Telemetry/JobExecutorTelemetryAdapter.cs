using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.Telemetry;

/// <summary>
/// Bridges the source-generated <see cref="JobExecutorTelemetryInstrumented"/> proxy
/// (which implements <see cref="IJobExecutorTelemetry"/>) back to the foundational
/// <see cref="IJobTypeExecutor"/> contract that the scheduling worker resolves.
/// </summary>
/// <remarks>
/// <para>
/// The adapter implements both interfaces using explicit interface implementations so
/// callers cast to whichever interface they need. The flow per execute:
/// </para>
/// <list type="number">
///   <item><description>Worker calls <see cref="IJobTypeExecutor.ExecuteAsync"/> on the adapter.</description></item>
///   <item><description>Adapter forwards to <see cref="JobExecutorTelemetryInstrumented.ExecuteAsync"/> (records spans/counters/histograms).</description></item>
///   <item><description>Proxy invokes <see cref="IJobExecutorTelemetry.ExecuteAsync"/> back on the adapter.</description></item>
///   <item><description>Adapter forwards to the actual inner <see cref="IJobTypeExecutor.ExecuteAsync"/>.</description></item>
/// </list>
/// <para>
/// <see cref="TypeName"/> and <see cref="MaxAttempts"/> are forwarded straight from the
/// inner executor — the worker reads <see cref="TypeName"/> to look up the executor for a
/// given job type and <see cref="MaxAttempts"/> to override the global retry policy.
/// </para>
/// </remarks>
internal sealed class JobExecutorTelemetryAdapter : IJobTypeExecutor, IJobExecutorTelemetry
{
    private readonly IJobTypeExecutor _inner;
    private readonly JobExecutorTelemetryInstrumented _proxy;

    public JobExecutorTelemetryAdapter(IJobTypeExecutor inner)
    {
        _inner = inner;
        // The proxy wraps `this` (which exposes IJobExecutorTelemetry by forwarding to _inner).
        _proxy = new JobExecutorTelemetryInstrumented(this);
    }

    // Worker reads TypeName/MaxAttempts straight from the inner executor.
    public string TypeName => _inner.TypeName;
    public int MaxAttempts => _inner.MaxAttempts;

    // IJobTypeExecutor.ExecuteAsync: route through the proxy (records spans/counters/histograms).
    ValueTask IJobTypeExecutor.ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
        => _proxy.ExecuteAsync(payload, ctx, ct);

    // IJobExecutorTelemetry.ExecuteAsync: invoked by the proxy → forwards to the actual inner executor.
    ValueTask IJobExecutorTelemetry.ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
        => _inner.ExecuteAsync(payload, ctx, ct);
}
