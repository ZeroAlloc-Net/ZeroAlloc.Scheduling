using BenchmarkDotNet.Attributes;
using Coravel;
using Coravel.Invocable;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ICoravelScheduler = Coravel.Scheduling.Schedule.Interfaces.IScheduler;

namespace ZeroAlloc.Scheduling.Benchmarks;

// Coravel is the de-facto in-process scheduler in .NET. Both libraries are
// lightweight, no-DB-by-default, in-process. This benchmark measures the
// apples-to-apples scenarios both support: enqueue cost (registration),
// dispatch (single invocation), and a 100-job burst.
//
// Cron parsing is internalized by both libraries — not exercised here.

public sealed class CoravelNoopInvocable : IInvocable
{
    public Task Invoke() => Task.CompletedTask;
}

[Job(MaxAttempts = 1)]
public sealed class ZaNoopJob : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
        => ValueTask.CompletedTask;
}

[MemoryDiagnoser]
[SimpleJob]
public class CoravelComparisonBenchmark
{
    private IServiceProvider _coravelServices = null!;
    private ICoravelScheduler _coravelScheduler = null!;
    private ZaNoopJob _zaJob = null!;
    private JobContext _zaCtx = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sc = new ServiceCollection();
        sc.AddScheduler();
        sc.AddTransient<CoravelNoopInvocable>();
        _coravelServices = sc.BuildServiceProvider();
        _coravelScheduler = _coravelServices.GetRequiredService<ICoravelScheduler>();

        _zaJob = new ZaNoopJob();
        _zaCtx = new JobContext
        {
            JobId = JobId.New(),
            Attempt = 1,
            ScheduledAt = DateTimeOffset.UtcNow,
            Services = _coravelServices,
        };
    }

    [GlobalCleanup]
    public void Cleanup() => (_coravelServices as IDisposable)?.Dispose();

    [Benchmark(Baseline = true, Description = "Coravel: Schedule invocable")]
    [BenchmarkCategory("Enqueue")]
    public void Coravel_Schedule()
    {
        _coravelScheduler.Schedule<CoravelNoopInvocable>().EverySeconds(5);
    }

    [Benchmark(Description = "ZA.Scheduling: ExecuteAsync (dispatch, no scheduler tick)")]
    [BenchmarkCategory("Dispatch")]
    public ValueTask Za_Dispatch()
        => _zaJob.ExecuteAsync(_zaCtx, CancellationToken.None);

    [Benchmark(Description = "Coravel: 100 schedule calls")]
    [BenchmarkCategory("Burst")]
    public void Coravel_Burst100()
    {
        for (var i = 0; i < 100; i++)
            _coravelScheduler.Schedule<CoravelNoopInvocable>().EverySeconds(5);
    }

    [Benchmark(Description = "ZA.Scheduling: 100 ExecuteAsync calls")]
    [BenchmarkCategory("Burst")]
    public async ValueTask Za_Burst100()
    {
        for (var i = 0; i < 100; i++)
            await _zaJob.ExecuteAsync(_zaCtx, CancellationToken.None).ConfigureAwait(false);
    }
}
