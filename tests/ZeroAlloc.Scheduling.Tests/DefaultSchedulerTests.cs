#pragma warning disable IL2026, IL3050 // DefaultScheduler uses reflection-based JSON serializer; acceptable in tests

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

public sealed class DefaultSchedulerTests
{
    private readonly InMemoryJobStore _store = new();
    private readonly IJobSerializer _serializer = new DefaultJobSerializer();
    private readonly IOptionsMonitor<SchedulingOptions> _options;

    public DefaultSchedulerTests()
    {
        var services = new ServiceCollection();
        services.AddOptions<SchedulingOptions>();
        _options = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<SchedulingOptions>>();
    }

    [Fact]
    public async Task EnqueueAsync_UsesPerJobMaxAttempts_WhenExecutorMaxAttemptsIsPositive()
    {
        var typeName = typeof(SendEmailJob).FullName!;
        var executor = new FakeExecutor(typeName, maxAttempts: 5);
        var scheduler = new DefaultScheduler(_store, _serializer, _options, [executor]);

        await scheduler.EnqueueAsync(new SendEmailJob());

        var entry = _store.AllEntries.Single();
        entry.MaxAttempts.Should().Be(5);
        entry.TypeName.Should().Be(typeName);
    }

    [Fact]
    public async Task EnqueueAsync_UsesGlobalDefault_WhenExecutorMaxAttemptsIsZero()
    {
        var typeName = typeof(SendEmailJob).FullName!;
        var executor = new FakeExecutor(typeName, maxAttempts: 0);
        var scheduler = new DefaultScheduler(_store, _serializer, _options, [executor]);

        await scheduler.EnqueueAsync(new SendEmailJob());

        var entry = _store.AllEntries.Single();
        entry.MaxAttempts.Should().Be(_options.CurrentValue.DefaultMaxAttempts);
    }

    [Fact]
    public async Task EnqueueAsync_WithDelay_SetsScheduledAtInFuture()
    {
        var scheduler = new DefaultScheduler(_store, _serializer, _options, []);
        var before = DateTimeOffset.UtcNow;

        await scheduler.EnqueueAsync(new SendEmailJob(), TimeSpan.FromMinutes(30));

        var entry = _store.AllEntries.Single();
        entry.ScheduledAt.Should().BeAfter(before.AddMinutes(29));
    }

    private sealed record SendEmailJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class FakeExecutor(string typeName, int maxAttempts) : IJobTypeExecutor
    {
        public string TypeName => typeName;
        public int MaxAttempts => maxAttempts;
        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
