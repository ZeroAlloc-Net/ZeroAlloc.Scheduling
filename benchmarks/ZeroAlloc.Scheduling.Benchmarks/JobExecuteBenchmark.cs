using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.Benchmarks;

// Measures the overhead of calling IJob.ExecuteAsync directly on a
// [Job]-annotated class. The claim: the generator-emitted executor adds
// zero allocations on the dispatch path beyond the JobContext (which is
// constructed once per execution by the scheduler, not once per benchmark
// iteration).
[MemoryDiagnoser]
[SimpleJob]
public class JobExecuteBenchmark
{
    private SendEmailJob _job = null!;
    private JobContext _ctx = null!;

    [GlobalSetup]
    public void Setup()
    {
        _job = new SendEmailJob { To = "benchmark@example.com" };
        _ctx = new JobContext
        {
            JobId = JobId.New(),
            Attempt = 1,
            ScheduledAt = System.DateTimeOffset.UtcNow,
            Services = new EmptyServiceProvider(),
        };
    }

    [Benchmark]
    public async ValueTask ExecuteAsync()
        => await _job.ExecuteAsync(_ctx, CancellationToken.None).ConfigureAwait(false);
}

[Job(MaxAttempts = 3)]
public sealed class SendEmailJob : IJob
{
    public string To { get; init; } = "";

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
        => ValueTask.CompletedTask;
}

internal sealed class EmptyServiceProvider : System.IServiceProvider
{
    public object? GetService(System.Type serviceType) => null;
}
