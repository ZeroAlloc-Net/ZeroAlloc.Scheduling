using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// Pins what <see cref="SchedulingWorkerService.ProcessEntryAsync"/> allocates per job on top of the
/// store: the <see cref="JobContext"/> handed to the job, and nothing else. No listener is attached
/// to the worker's meter or activity source, so metric tags and activity tags must cost nothing,
/// and a recurring job must not re-parse its cron expression on every run.
/// </summary>
public sealed class WorkerAllocationTests
{
    private const int Jobs = 1_000;
    private const string Cron = "*/5 * * * *";

    private static JobContext? _contextSink;

    private readonly ITestOutputHelper _output;

    public WorkerAllocationTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessEntryAsync_AllocatesOnlyTheJobContext_OnTopOfTheStore(bool recurring)
    {
        var cron = recurring ? Cron : null;
        var services = new ServiceCollection().BuildServiceProvider();
        var worker = new SchedulingWorkerService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(new SchedulingOptions()),
            NullLogger<SchedulingWorkerService>.Instance,
            [new NoOpExecutor()]);

        // Warm up every path once: tiered JIT, the cron cache, static instrument initialisation.
        using (var warmStore = new InMemoryJobStore())
        {
            foreach (var entry in await ClaimAsync(warmStore, 2, cron))
                await worker.ProcessEntryAsync(warmStore, entry, services, CancellationToken.None);
            foreach (var entry in await ClaimAsync(warmStore, 2, cron))
                await warmStore.MarkSucceededAsync(entry.Id, NextRun(cron), cron, 3, CancellationToken.None);
        }
        long contextBytes = MeasureJobContext(services);

        // The store's own cost for the same work: marking N claimed jobs succeeded, and for a
        // recurring job inserting the next occurrence.
        using var storeOnly = new InMemoryJobStore();
        var storeEntries = await ClaimAsync(storeOnly, Jobs, cron);
        var nextRun = NextRun(cron);
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var entry in storeEntries)
            _ = storeOnly.MarkSucceededAsync(entry.Id, nextRun, cron, 3, CancellationToken.None);
        long storeBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        using var store = new InMemoryJobStore();
        var entries = await ClaimAsync(store, Jobs, cron);
        bool allSynchronous = true;
        before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var entry in entries)
            allSynchronous &= worker.ProcessEntryAsync(store, entry, services, CancellationToken.None).IsCompletedSuccessfully;
        long workerBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        allSynchronous.Should().BeTrue("the measurement is only valid when every call stays on this thread");
        store.AllEntries.Count(e => e.Status == JobStatus.Succeeded).Should().Be(Jobs);

        double perJob = (double)(workerBytes - storeBytes) / Jobs;
        _output.WriteLine(
            $"recurring={recurring}: worker {workerBytes / (double)Jobs:F1} B/job, store {storeBytes / (double)Jobs:F1} B/job, " +
            $"worker overhead {perJob:F1} B/job, JobContext {contextBytes} B");

        perJob.Should().BeLessThanOrEqualTo(contextBytes,
            "the worker's only per-job allocation is the JobContext handed to the job");
    }

    // Lives in this class so it never runs alongside the allocation test: a MeterListener enables
    // the worker's instruments process-wide, and the allocation test relies on them being off.
    [Fact]
    public async Task ProcessEntryAsync_WithAMeterListener_StillRecordsTheTaggedMeasurements()
    {
        var measurements = new List<(string Instrument, object? Attempt)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Scheduling", StringComparison.Ordinal))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((i, _, tags, _) => Record(i.Name, tags));
        listener.SetMeasurementEventCallback<double>((i, _, tags, _) => Record(i.Name, tags));
        listener.Start();

        var services = new ServiceCollection().BuildServiceProvider();
        var worker = new SchedulingWorkerService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(new SchedulingOptions()),
            NullLogger<SchedulingWorkerService>.Instance,
            [new NoOpExecutor()]);
        using var store = new InMemoryJobStore();
        var entry = (await ClaimAsync(store, 1, Cron))[0];

        await worker.ProcessEntryAsync(store, entry, services, CancellationToken.None);

        lock (measurements)
        {
            measurements.Should().Contain(("scheduling.job_executions", 1));
            measurements.Should().Contain(("scheduling.job_duration_ms", 1));
        }

        void Record(string instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            object? attempt = null;
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "job.attempt", StringComparison.Ordinal))
                    attempt = tag.Value;
            }
            lock (measurements)
                measurements.Add((instrument, attempt));
        }
    }

    private static long MeasureJobContext(IServiceProvider services)
    {
        _contextSink = NewContext(services);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _contextSink = NewContext(services);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static JobContext NewContext(IServiceProvider services) => new()
    {
        JobId = JobId.New(),
        Attempt = 1,
        ScheduledAt = DateTimeOffset.UtcNow,
        Services = services,
    };

    private static DateTimeOffset? NextRun(string? cron)
        => cron is null
            ? null
            : Cronos.CronExpression.Parse(cron).GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

    private static async Task<IReadOnlyList<JobEntry>> ClaimAsync(InMemoryJobStore store, int count, string? cron)
    {
        for (int i = 0; i < count; i++)
            await store.EnqueueAsync(NoOpExecutor.Name, [], DateTimeOffset.UtcNow.AddSeconds(-1), 3, cron, CancellationToken.None).ConfigureAwait(false);
        var claimed = await store.FetchPendingAsync(count, CancellationToken.None).ConfigureAwait(false);
        claimed.Should().HaveCount(count);
        return claimed;
    }

    private sealed class NoOpExecutor : IJobTypeExecutor
    {
        public const string Name = "NoOp";
        public string TypeName => Name;
        public int MaxAttempts => 0;
        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class StaticOptionsMonitor(SchedulingOptions value) : IOptionsMonitor<SchedulingOptions>
    {
        public SchedulingOptions CurrentValue => value;
        public SchedulingOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SchedulingOptions, string?> listener) => null;
    }
}
