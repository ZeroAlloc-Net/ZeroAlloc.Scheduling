namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>A recurring job that never falls due within a test, for the re-seed tests.</summary>
[Job(Cron = "0 0 1 1 *")]
public sealed class YearlySweep : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
}
