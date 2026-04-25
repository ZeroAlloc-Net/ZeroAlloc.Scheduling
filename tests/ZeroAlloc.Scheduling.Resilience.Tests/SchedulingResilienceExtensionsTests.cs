using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.Resilience;

namespace ZeroAlloc.Scheduling.Resilience.Tests;

/// <summary>
/// Validates that AddSchedulingResilience wires a proxy as IJobTypeExecutor.
/// Uses a hand-written proxy to represent what the Resilience generator would emit.
/// </summary>
public class SchedulingResilienceExtensionsTests
{
    [Fact]
    public void AddSchedulingResilience_RegistersProxyAsExecutor()
    {
        var services = new ServiceCollection();
        services.AddTransient<SendEmailJobExecutorImpl>();
        services.AddSchedulingResilience<ISendEmailJobExecutor, SendEmailJobExecutorProxy>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IJobTypeExecutor>();

        executor.Should().BeOfType<SendEmailJobExecutorProxy>();
    }

    [Fact]
    public async Task AddSchedulingResilience_ProxyDelegatesToInnerExecutor()
    {
        var services = new ServiceCollection();
        services.AddTransient<SendEmailJobExecutorImpl>();
        services.AddSchedulingResilience<ISendEmailJobExecutor, SendEmailJobExecutorProxy>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IJobTypeExecutor>();
        var proxy = (SendEmailJobExecutorProxy)executor;

        var payload = System.Text.Encoding.UTF8.GetBytes("{}");
        var ctx = new JobContext
        {
            JobId = new JobId(Guid.NewGuid()),
            Attempt = 1,
            ScheduledAt = DateTimeOffset.UtcNow,
            Services = new ServiceCollection().BuildServiceProvider(),
        };

        await executor.ExecuteAsync(payload, ctx, CancellationToken.None);

        proxy.Inner.CallCount.Should().Be(1);
    }

    // ---- Supporting types ----

    private interface ISendEmailJobExecutor : IJobTypeExecutor { }

    private sealed class SendEmailJobExecutorImpl : ISendEmailJobExecutor
    {
        public int CallCount { get; private set; }
        public string TypeName => "SendEmail";
        public int MaxAttempts => 0;

        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    // Simulates what the Resilience source generator would emit for ISendEmailJobExecutor.
    private sealed class SendEmailJobExecutorProxy : ISendEmailJobExecutor
    {
        public SendEmailJobExecutorImpl Inner { get; }

        public SendEmailJobExecutorProxy(SendEmailJobExecutorImpl inner) => Inner = inner;

        public string TypeName => Inner.TypeName;
        public int MaxAttempts => Inner.MaxAttempts;

        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
            => Inner.ExecuteAsync(payload, ctx, ct);
    }
}
