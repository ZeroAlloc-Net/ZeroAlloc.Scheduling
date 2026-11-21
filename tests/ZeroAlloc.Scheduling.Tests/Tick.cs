namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// A recurring job without instance fields, and without <c>[ZeroAllocSerializable]</c>: the
/// generator gives it an executor and a recurring seed that need no <see cref="IJobSerializer"/>.
/// </summary>
[Job(Every = Every.Minute)]
public sealed class Tick : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        ctx.Services.GetService(typeof(TickSink)).Should().BeOfType<TickSink>()
            .Which.Ran.TrySetResult(ctx.JobId);
        return ValueTask.CompletedTask;
    }
}
