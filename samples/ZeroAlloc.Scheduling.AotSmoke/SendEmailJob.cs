using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.AotSmoke;

[Job(MaxAttempts = 3)]
public sealed class SendEmailJob : IJob
{
    public string To { get; init; } = "";

    // Static counter is kept non-public to satisfy MA0069 — the smoke reads it via the
    // internal accessor below rather than touching the field directly.
    private static int s_invocationCount;
    internal static int InvocationCount => Volatile.Read(ref s_invocationCount);
    internal static void ResetInvocationCount() => Volatile.Write(ref s_invocationCount, 0);

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref s_invocationCount);
        return ValueTask.CompletedTask;
    }
}
