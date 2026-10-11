using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.Benchmarks;

// Measures calling IJob.ExecuteAsync directly on a [Job]-annotated class, with a
// ready-made job and JobContext. It is the floor under a job's cost: it never touches
// the generated executor, the serializer or the worker. WorkerDispatchBenchmark measures
// those.
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
