using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Scheduling.AotSmoke;

// Runs SendEmailJob by hand, in place of its generated executor. The first call fails, so the
// job only completes when the resilience proxy retries it.
internal sealed class ResilientSendEmailExecutor(IJobSerializer serializer) : IResilientSendEmailExecutor
{
    private int _calls;

    public string TypeName => typeof(SendEmailJob).FullName!;

    public int MaxAttempts => 1;

    public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _calls) == 1)
            throw new System.InvalidOperationException("first attempt fails so the proxy retries");
        return serializer.Deserialize<SendEmailJob>(payload).ExecuteAsync(ctx, ct);
    }
}
