using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.Resilience;

namespace ZeroAlloc.Scheduling.Resilience.Tests;

/// <summary>
/// Validates that the <see cref="ISchedulingBuilder"/>-shaped <c>WithResilience</c> entrypoint
/// wires the Resilience proxy as <see cref="IJobTypeExecutor"/>, and the legacy
/// <c>AddSchedulingResilience</c> shim still works.
/// </summary>
public class WithResilienceTests
{
    [Fact]
    public void WithResilience_RegistersProxyAsExecutor()
    {
        var services = new ServiceCollection();
        services.AddTransient<SendEmailJobExecutorImpl>();

#pragma warning disable IL2026, IL3050
        services.AddScheduling()
                .WithResilience<ISendEmailJobExecutor, SendEmailJobExecutorProxy>();
#pragma warning restore IL2026, IL3050

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IJobTypeExecutor>()
                .Should().BeOfType<SendEmailJobExecutorProxy>();
    }

    [Fact]
    public void AddSchedulingResilience_LegacyShim_RegistersProxyAsExecutor()
    {
        var services = new ServiceCollection();
        services.AddTransient<SendEmailJobExecutorImpl>();

        services.AddSchedulingResilience<ISendEmailJobExecutor, SendEmailJobExecutorProxy>();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IJobTypeExecutor>()
                .Should().BeOfType<SendEmailJobExecutorProxy>();
    }

    // ---- Supporting types (mirror SchedulingResilienceExtensionsTests) ----

    public interface ISendEmailJobExecutor : IJobTypeExecutor { }

    public sealed class SendEmailJobExecutorImpl : ISendEmailJobExecutor
    {
        public string TypeName => "SendEmail";
        public int MaxAttempts => 0;

        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
            => ValueTask.CompletedTask;
    }

    public sealed class SendEmailJobExecutorProxy : ISendEmailJobExecutor
    {
        public SendEmailJobExecutorImpl Inner { get; }
        public SendEmailJobExecutorProxy(SendEmailJobExecutorImpl inner) => Inner = inner;
        public string TypeName => Inner.TypeName;
        public int MaxAttempts => Inner.MaxAttempts;
        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
            => Inner.ExecuteAsync(payload, ctx, ct);
    }
}
