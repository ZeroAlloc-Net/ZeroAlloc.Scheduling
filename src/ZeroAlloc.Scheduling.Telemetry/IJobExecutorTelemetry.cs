using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Telemetry;

namespace ZeroAlloc.Scheduling.Telemetry;

/// <summary>
/// Marker interface picked up by ZeroAlloc.Telemetry's [Instrument] source generator.
/// The generator emits <c>JobExecutorTelemetryInstrumented</c> implementing this interface;
/// the proxy wraps an instance and records spans, counters, and histograms on every
/// <see cref="ExecuteAsync"/> call.
/// </summary>
/// <remarks>
/// Standalone interface (does NOT inherit <c>IJobTypeExecutor</c>) — the source generator
/// only walks members declared directly on the [Instrument] interface. A small adapter
/// (in this package, written in Task 2.4) bridges the generated proxy back to <c>IJobTypeExecutor</c>.
/// </remarks>
[Instrument("ZeroAlloc.Scheduling")]
public interface IJobExecutorTelemetry
{
    [Trace("scheduling.job_execute")]
    [Count("scheduling.jobs_total")]
    [Histogram("scheduling.job_duration_ms")]
    ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct);
}
