namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// A job without state whose first construction throws, as one would while a dependency it
/// touches at construction is down.
/// </summary>
[Job]
public sealed class FlakyStart : IJob
{
    private static int s_constructions;

    public FlakyStart()
    {
        if (Interlocked.Increment(ref s_constructions) == 1)
            throw new InvalidOperationException("first construction fails");
    }

    public static int Runs => s_runs;

    private static int s_runs;

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref s_runs);
        return ValueTask.CompletedTask;
    }
}
