namespace ZeroAlloc.Scheduling.Tests;

/// <summary>Receives the message of the <see cref="Ping"/> the worker ran.</summary>
public sealed class PingSink
{
    public TaskCompletionSource<string> Received { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
