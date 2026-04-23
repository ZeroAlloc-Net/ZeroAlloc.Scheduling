using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.AotSmoke;

[Job(MaxAttempts = 3)]
public sealed class SendEmailJob : IJob
{
    public string To { get; init; } = "";

    public static int InvocationCount;

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref InvocationCount);
        return ValueTask.CompletedTask;
    }
}
