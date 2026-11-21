namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>An executor whose job throws on every attempt.</summary>
internal sealed class AlwaysFailingExecutor(string typeName) : IJobTypeExecutor
{
    private int _calls;

    public string TypeName => typeName;

    public int MaxAttempts => 0;

    public int Calls => Volatile.Read(ref _calls);

    public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        throw new InvalidOperationException("The job fails on every attempt.");
    }
}
