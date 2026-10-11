using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.Benchmarks;

// Measures the path a job really takes once the store has handed it over, as an application wires
// it: AddSerializerDispatcher() + AddScheduling() + the generated AddReportJob(). JobExecuteBenchmark
// only times IJob.ExecuteAsync on a ready-made job; these three add, in order:
//
//   GeneratedExecutor   the generator-emitted IJobTypeExecutor: DispatchingJobSerializer
//                       deserializes the payload, then the job runs.
//   WorkerProcessEntry  the worker's per-job path around it: executor lookup, the JobContext,
//                       the activity and metrics, MarkSucceededAsync and, for a recurring job,
//                       the next cron occurrence.
//   WorkerProcessBatch  one poll: the service scope, resolving the store, FetchPendingAsync and
//                       one job through WorkerProcessEntry.
//
// The store replays one claimed entry and does no work of its own, so the numbers are the
// scheduler's cost alone; a real store adds its I/O on top. No meter or activity listener is
// attached, as in an application that has not enabled telemetry.
[MemoryDiagnoser]
[SimpleJob]
public class WorkerDispatchBenchmark
{
    private ServiceProvider _services = null!;
    private SchedulingWorkerService _worker = null!;
    private ReplayJobStore _store = null!;
    private IJobTypeExecutor _executor = null!;
    private JobEntry _entry = null!;
    private JobContext _ctx = null!;

    [Params(false, true)]
    public bool Recurring { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSerializerDispatcher();
        services.AddScheduling().AddReportJob();
        services.AddSingleton<ReplayJobStore>();
        services.AddSingleton<IJobStore>(sp => sp.GetRequiredService<ReplayJobStore>());
        _services = services.BuildServiceProvider();

        var payload = _services.GetRequiredService<IJobSerializer>().Serialize(new ReportJob { ReportId = 42 });
        _executor = _services.GetServices<IJobTypeExecutor>().Single();
        _entry = new JobEntry
        {
            Id = JobId.New(),
            TypeName = _executor.TypeName,
            Payload = payload,
            Status = JobStatus.Running,
            Attempts = 0,
            MaxAttempts = 3,
            ScheduledAt = DateTimeOffset.UtcNow,
            CronExpression = Recurring ? "*/5 * * * *" : null,
        };
        _store = _services.GetRequiredService<ReplayJobStore>();
        _store.Entries = [_entry];
        _worker = _services.GetRequiredService<SchedulingWorkerService>();
        _ctx = new JobContext
        {
            JobId = _entry.Id,
            Attempt = 1,
            ScheduledAt = _entry.ScheduledAt,
            Services = _services,
        };
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();

    [Benchmark]
    public ValueTask GeneratedExecutor() => _executor.ExecuteAsync(_entry.Payload, _ctx, CancellationToken.None);

    [Benchmark]
    public Task WorkerProcessEntry() => _worker.ProcessEntryAsync(_store, _entry, _services, CancellationToken.None);

    [Benchmark]
    public Task WorkerProcessBatch() => _worker.ProcessBatchAsync(CancellationToken.None);
}

[Job(MaxAttempts = 3)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class ReportJob : IJob
{
    public int ReportId { get; init; }

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
}

[JsonSerializable(typeof(ReportJob))]
internal sealed partial class ReportJobJsonContext : JsonSerializerContext;

// Hands the same claimed entry to every poll and accepts every state change without doing
// anything, so the benchmark measures the worker and not a store.
internal sealed class ReplayJobStore : IJobStore
{
    public IReadOnlyList<JobEntry> Entries { get; set; } = [];

    public ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
        => new(Entries);

    public ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct)
        => ValueTask.CompletedTask;
}
