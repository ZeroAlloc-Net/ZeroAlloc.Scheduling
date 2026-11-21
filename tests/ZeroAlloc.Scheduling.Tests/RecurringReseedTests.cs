using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// Issue #602: the generated hosted service of a recurring job seeds it again every
/// <see cref="SchedulingOptions.ClaimTimeout"/> while the host runs, so a next occurrence that a
/// failed write lost comes back without a restart.
/// </summary>
public sealed class RecurringReseedTests
{
    private static readonly TimeSpan ReseedInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_Recurring_Job_Whose_Next_Occurrence_Was_Lost_Comes_Back_Without_A_Restart()
    {
        using var inner = new InMemoryJobStore();
        var tickType = typeof(Tick).FullName!;
        var cron = EveryToCron.Convert(Every.Minute);
        // A run due now, so the worker runs it at once. The seed at startup sees it and adds none.
        await inner.UpsertRecurringAsync(tickType, [], DateTimeOffset.UtcNow.AddSeconds(-1), cron, 3, default);
        var store = new RecordingStore(inner) { DropNextOccurrence = true };
        using var host = BuildHost(store, o => o.PollingInterval = TimeSpan.FromMilliseconds(20));

        await host.StartAsync();
        try
        {
            var ran = await host.Services.GetRequiredService<TickSink>().Ran.Task.WaitAsync(Deadline);

            var next = await WaitForAsync(async () =>
                (await inner.GetRecurringAsync().ConfigureAwait(false)).SingleOrDefault(static e => e.Status == JobStatus.Pending));

            next.Id.Should().NotBe(ran, "the run that succeeded is history");
            next.TypeName.Should().Be(tickType);
            next.CronExpression.Should().Be(cron);
            next.ScheduledAt.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(-1), "the seed schedules the next occurrence");
            store.DroppedNextOccurrences.Should().BePositive("the success lost its next occurrence");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task A_Failed_Reseed_Is_Logged_And_Tried_Again_One_Interval_Later()
    {
        using var inner = new InMemoryJobStore();
        var store = new RecordingStore(inner) { FailingUpsert = 2 };
        var logs = new CapturingLoggerProvider();
        using var host = BuildHost(store, logging: logs);

        await host.StartAsync();
        try
        {
            await WaitForAsync(() => Task.FromResult(store.Upserts >= 3 ? (object?)true : null));
        }
        finally
        {
            await host.StopAsync();
        }

        var error = logs.Entries.Should().ContainSingle(static e => e.Level == LogLevel.Error).Subject;
        error.Category.Should().Be(typeof(Tick).FullName + "RecurringStartup");
        error.Message.Should().Be(
            $"Re-seeding recurring job {typeof(Tick).FullName} failed; it is tried again after SchedulingOptions.ClaimTimeout.");
        error.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Stopping_The_Host_Stops_The_Reseed()
    {
        using var inner = new InMemoryJobStore();
        var store = new RecordingStore(inner);
        using var host = BuildHost(store);

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(store.Upserts >= 2 ? (object?)true : null));
        await host.StopAsync();
        var afterStop = store.Upserts;

        await Task.Delay(ReseedInterval * 5);

        store.Upserts.Should().Be(afterStop);
    }

    [Fact]
    public async Task A_Claim_Timeout_Longer_Than_Task_Delay_Takes_Does_Not_Fail_The_Host()
    {
        using var inner = new InMemoryJobStore();
        var store = new RecordingStore(inner);
        var logs = new CapturingLoggerProvider();
        // The seed reads the options once for its maximum attempts, and the loop once more for the
        // interval right before its first delay, so a second read shows the loop got that far.
        var options = new CountingOptions(new SchedulingOptions { ClaimTimeout = TimeSpan.MaxValue });
        using var host = BuildHost(store, logging: logs, options: options);

        await host.StartAsync();
        try
        {
            await WaitForAsync(() => Task.FromResult(options.Reads >= 2 ? (object?)true : null));
            await Task.Delay(ReseedInterval);
        }
        finally
        {
            await host.StopAsync();
        }

        store.Upserts.Should().Be(1, "only the startup seed runs within the test");
        logs.Entries.Should().NotContain(static e => e.Level >= LogLevel.Error, "a delay out of range would throw and be logged");
    }

    [Fact]
    public async Task A_Claim_Timeout_Below_A_Millisecond_Waits_Between_Seeds()
    {
        using var inner = new InMemoryJobStore();
        var store = new RecordingStore(inner);
        using var host = BuildHost(store, o => o.ClaimTimeout = TimeSpan.FromTicks(1));

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(store.Upserts >= 3 ? (object?)true : null));
        await Task.Delay(ReseedInterval);
        await host.StopAsync();

        // Without a wait the loop would seed without pause, many thousands of times in this span.
        store.Upserts.Should().BeLessThan(1000, "the loop waits at least a millisecond between seeds");
    }

    private static IHost BuildHost(
        RecordingStore store,
        Action<SchedulingOptions>? configure = null,
        CapturingLoggerProvider? logging = null,
        IOptions<SchedulingOptions>? options = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging(l =>
        {
            if (logging is not null)
                l.AddProvider(logging);
        });
        builder.Services.AddSingleton<TickSink>();
        builder.Services.AddSingleton<IJobStore>(store);
        builder.Services
            .AddScheduling(o =>
            {
                o.PollingInterval = TimeSpan.FromMinutes(1);
                o.ClaimTimeout = ReseedInterval;
                configure?.Invoke(o);
            })
            .AddTickJob();
        if (options is not null)
            builder.Services.AddSingleton(options);
        return builder.Build();
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe)
        where T : class
    {
        var until = DateTime.UtcNow + Deadline;
        while (true)
        {
            if (await probe().ConfigureAwait(false) is { } result)
                return result;
            if (DateTime.UtcNow > until)
                throw new TimeoutException("The condition did not hold within the deadline.");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// An in-memory store that counts seeds, can fail one of them, and can lose the next occurrence
    /// of a run that succeeded, as a failed insert after the success would.
    /// </summary>
    private sealed class RecordingStore(InMemoryJobStore inner) : IJobStore
    {
        private int _upserts;
        private int _dropped;

        public bool DropNextOccurrence { get; init; }

        /// <summary>The 1-based seed that throws; 0 for none.</summary>
        public int FailingUpsert { get; init; }

        public int Upserts => Volatile.Read(ref _upserts);

        public int DroppedNextOccurrences => Volatile.Read(ref _dropped);

        public ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct)
            => inner.EnqueueAsync(typeName, payload, scheduledAt, maxAttempts, cronExpression, ct);

        public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
            => inner.FetchPendingAsync(batchSize, ct);

        public ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        {
            if (DropNextOccurrence && nextRunAt is not null)
            {
                Interlocked.Increment(ref _dropped);
                nextRunAt = null;
            }

            return inner.MarkSucceededAsync(id, nextRunAt, cronExpression, maxAttempts, ct);
        }

        public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
            => inner.MarkFailedAsync(id, attempts, nextRetryAt, ct);

        public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
            => inner.DeadLetterAsync(id, error, ct);

        public ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _upserts) == FailingUpsert)
                throw new InvalidOperationException("The store is unreachable.");

            return inner.UpsertRecurringAsync(typeName, payload, scheduledAt, cronExpression, maxAttempts, ct);
        }
    }

    /// <summary>Fixed options that count how often the seed service reads them.</summary>
    private sealed class CountingOptions(SchedulingOptions value) : IOptions<SchedulingOptions>
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public SchedulingOptions Value
        {
            get
            {
                Interlocked.Increment(ref _reads);
                return value;
            }
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
