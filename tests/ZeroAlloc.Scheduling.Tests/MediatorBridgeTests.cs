#pragma warning disable IL2026, IL3050 // DefaultJobSerializer uses reflection-based JSON; acceptable in tests

using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Scheduling.InMemory;
using ZeroAlloc.Scheduling.Mediator;

namespace ZeroAlloc.Scheduling.Tests;

public sealed class MediatorBridgeTests
{
    [Fact]
    public async Task MediatorExecutor_DispatchesJobViaHandler()
    {
        bool handled = false;

        var serializer = new DefaultJobSerializer();
        var handler = new FakeHandler(() =>
        {
            handled = true;
            return ValueTask.FromResult(Unit.Value);
        });

        var executor = new MediatorJobTypeExecutor<SendTestJobRequest>(serializer, handler);

        var payload = serializer.Serialize(new SendTestJobRequest());

        var services = new ServiceCollection()
            .AddScheduling()
            .AddSchedulingInMemory()
            .BuildServiceProvider();

        var ctx = new JobContext
        {
            JobId = JobId.New(),
            Attempt = 1,
            ScheduledAt = DateTimeOffset.UtcNow,
            Services = services,
        };

        await executor.ExecuteAsync(payload, ctx, CancellationToken.None);

        Assert.True(handled);
    }

    private sealed record SendTestJobRequest : IJob, IRequest<Unit>
    {
        public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class FakeHandler(Func<ValueTask<Unit>> fn) : IRequestHandler<SendTestJobRequest, Unit>
    {
        public ValueTask<Unit> Handle(SendTestJobRequest request, CancellationToken ct) => fn();
    }
}
