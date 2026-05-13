using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.Benchmarks;

// Measures what ZA.Scheduling's attribute-driven dispatch costs ON TOP OF the
// "naive" baseline that most apps run before reaching for a scheduler library:
// a plain method call invoked from a BackgroundService + Timer. The benchmark
// scaffolding itself plays the Timer's role — both rows just invoke a no-op
// method body.

[MemoryDiagnoser]
[SimpleJob]
public class NaiveTimerOverheadBenchmark
{
    private ZaNoopJob _zaJob = null!;
    private JobContext _zaCtx = null!;

    [GlobalSetup]
    public void Setup()
    {
        _zaJob = new ZaNoopJob();
        _zaCtx = new JobContext
        {
            JobId = JobId.New(),
            Attempt = 1,
            ScheduledAt = System.DateTimeOffset.UtcNow,
            Services = new EmptyServiceProvider(),
        };
    }

    [Benchmark(Baseline = true, Description = "Naive: direct method call")]
    public ValueTask Naive_DirectCall() => NoopAsync();

    [Benchmark(Description = "ZA.Scheduling: ExecuteAsync dispatch")]
    public ValueTask Za_Dispatch()
        => _zaJob.ExecuteAsync(_zaCtx, CancellationToken.None);

    private static ValueTask NoopAsync() => ValueTask.CompletedTask;
}
