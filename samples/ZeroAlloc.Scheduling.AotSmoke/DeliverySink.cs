using System.Threading.Tasks;

namespace ZeroAlloc.Scheduling.AotSmoke;

/// <summary>Receives the recipient of the <see cref="SendEmailJob"/> the worker ran.</summary>
internal sealed class DeliverySink
{
    public TaskCompletionSource<string> Delivered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
