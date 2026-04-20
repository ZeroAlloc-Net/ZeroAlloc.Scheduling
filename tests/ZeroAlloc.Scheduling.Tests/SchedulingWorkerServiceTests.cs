using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

public sealed class SchedulingWorkerServiceTests
{
    [Fact]
    public async Task Worker_ExecutesPendingJob_AndMarksSucceeded()
    {
        var store = new InMemoryJobStore();
        bool executed = false;

        var executor = new FakeExecutor("TestJob", (_, _) =>
        {
            executed = true;
            return ValueTask.CompletedTask;
        });

        var services = new ServiceCollection()
            .AddSingleton<IJobStore>(store)
            .AddSingleton<IJobTypeExecutor>(executor)
            .AddScheduling(o => o.PollingInterval = TimeSpan.FromMilliseconds(50))
            .BuildServiceProvider();

        await store.EnqueueAsync("TestJob", [], DateTimeOffset.UtcNow, 3, null, CancellationToken.None);

        var worker = services.GetRequiredService<SchedulingWorkerService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        executed.Should().BeTrue();
        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.Succeeded);
    }

    [Fact]
    public async Task Worker_OnExecutorFailure_DeadLettersAfterMaxAttempts()
    {
        var store = new InMemoryJobStore();
        int callCount = 0;

        var executor = new FakeExecutor("FailJob", (_, _) =>
        {
            callCount++;
            throw new InvalidOperationException("simulated failure");
        });

        var services = new ServiceCollection()
            .AddSingleton<IJobStore>(store)
            .AddSingleton<IJobTypeExecutor>(executor)
            .AddScheduling(o =>
            {
                o.PollingInterval = TimeSpan.FromMilliseconds(50);
                o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                o.DefaultMaxAttempts = 3;
            })
            .BuildServiceProvider();

        await store.EnqueueAsync("FailJob", [], DateTimeOffset.UtcNow, 3, null, CancellationToken.None);

        var worker = services.GetRequiredService<SchedulingWorkerService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.DeadLetter);
    }

    private sealed class FakeExecutor : IJobTypeExecutor
    {
        private readonly Func<byte[], JobContext, ValueTask> _fn;
        public FakeExecutor(string typeName, Func<byte[], JobContext, ValueTask> fn)
        {
            TypeName = typeName;
            _fn = fn;
        }
        public string TypeName { get; }
        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
            => _fn(payload, ctx);
    }
}
