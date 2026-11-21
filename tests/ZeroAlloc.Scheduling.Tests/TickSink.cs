namespace ZeroAlloc.Scheduling.Tests;

/// <summary>Receives the id of the <see cref="Tick"/> run the worker made.</summary>
public sealed class TickSink
{
    public TaskCompletionSource<JobId> Ran { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
