using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Scheduling.AotSmoke;

/// <summary>
/// A recurring job that never falls due during the smoke. Its generated hosted service seeds it at
/// startup and again every ClaimTimeout, which the re-seed scenario runs under AOT.
/// </summary>
[Job(Cron = "0 0 1 1 *")]
public sealed class YearlySweepJob : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
}
