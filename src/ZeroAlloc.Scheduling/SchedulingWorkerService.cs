using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Scheduling;

/// <summary>Polling worker that claims pending jobs and dispatches them via registered <see cref="IJobTypeExecutor"/> implementations.</summary>
public sealed class SchedulingWorkerService : BackgroundService
{
    private static readonly ActivitySource _activitySource = new("ZeroAlloc.Scheduling");
    private static readonly Meter _meter = new("ZeroAlloc.Scheduling");
    private static readonly Counter<long> _jobExecutions = _meter.CreateCounter<long>("scheduling.job_executions");
    private static readonly Counter<long> _jobRetries = _meter.CreateCounter<long>("scheduling.job_retries");
    private static readonly Counter<long> _jobDeadLetters = _meter.CreateCounter<long>("scheduling.job_dead_letters");
    private static readonly Histogram<double> _jobDurationMs = _meter.CreateHistogram<double>("scheduling.job_duration_ms");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly ILogger<SchedulingWorkerService> _logger;
    private readonly IReadOnlyDictionary<string, IJobTypeExecutor> _executors;

    public SchedulingWorkerService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SchedulingOptions> options,
        ILogger<SchedulingWorkerService> logger,
        IEnumerable<IJobTypeExecutor> executors)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _executors = executors.ToDictionary(e => e.TypeName, StringComparer.Ordinal);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Unhandled error in scheduling worker batch.");
            }

            try
            {
                await Task.Delay(_options.CurrentValue.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
#pragma warning disable MA0004
        await using var scope = _scopeFactory.CreateAsyncScope();
#pragma warning restore MA0004
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        var entries = await store.FetchPendingAsync(_options.CurrentValue.BatchSize, ct).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessEntryAsync(store, _executors, entry, scope.ServiceProvider, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessEntryAsync(
        IJobStore store,
        IReadOnlyDictionary<string, IJobTypeExecutor> executors,
        JobEntry entry,
        IServiceProvider scopeServices,
        CancellationToken ct)
    {
        if (!executors.TryGetValue(entry.TypeName, out var executor))
        {
            _logger.LogWarning("No executor for job type '{TypeName}'. Dead-lettering {Id}.", entry.TypeName, entry.Id);
            _jobDeadLetters.Add(1, new TagList { { "job.type", entry.TypeName } });
            await store.DeadLetterAsync(entry.Id, $"No executor for type '{entry.TypeName}'.", ct).ConfigureAwait(false);
            return;
        }

        int attempt = entry.Attempts + 1;
        var ctx = new JobContext
        {
            JobId = entry.Id,
            Attempt = attempt,
            ScheduledAt = entry.ScheduledAt,
            Services = scopeServices,
        };

        using var activity = _activitySource.StartActivity("scheduling.job");
        activity?.SetTag("job.type", entry.TypeName);
        activity?.SetTag("job.attempt", attempt);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await executor.ExecuteAsync(entry.Payload, ctx, ct).ConfigureAwait(false);

            // Compute next run for recurring jobs
            DateTimeOffset? nextRunAt = null;
            if (entry.CronExpression != null)
                nextRunAt = ComputeNextRun(entry.CronExpression);

            await store.MarkSucceededAsync(entry.Id, nextRunAt, entry.CronExpression, entry.MaxAttempts, ct).ConfigureAwait(false);

            _jobExecutions.Add(1, new TagList { { "job.type", entry.TypeName }, { "job.attempt", attempt } });
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await HandleJobFailureAsync(store, entry, ex, ct).ConfigureAwait(false);
        }
        finally
        {
            _jobDurationMs.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                new TagList { { "job.type", entry.TypeName }, { "job.attempt", attempt } });
        }
    }

    private async Task HandleJobFailureAsync(IJobStore store, JobEntry entry, Exception ex, CancellationToken ct)
    {
        int newAttempts = entry.Attempts + 1;
        if (newAttempts >= entry.MaxAttempts)
        {
            _logger.LogError(ex, "Job {Id} ({TypeName}) dead-lettered after {Max} attempts.", entry.Id, entry.TypeName, entry.MaxAttempts);
            _jobDeadLetters.Add(1, new TagList { { "job.type", entry.TypeName } });
            await store.DeadLetterAsync(entry.Id, ex.Message, ct).ConfigureAwait(false);
        }
        else
        {
            var delay = TimeSpan.FromMilliseconds(_options.CurrentValue.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, newAttempts - 1));
            var nextRetry = DateTimeOffset.UtcNow.Add(delay);
            _logger.LogWarning(ex, "Job {Id} ({TypeName}) failed (attempt {A}/{Max}). Retry at {Next}.",
                entry.Id, entry.TypeName, newAttempts, entry.MaxAttempts, nextRetry);
            _jobRetries.Add(1, new TagList { { "job.type", entry.TypeName }, { "job.attempt", newAttempts } });
            await store.MarkFailedAsync(entry.Id, newAttempts, nextRetry, ct).ConfigureAwait(false);
        }
    }

    private static DateTimeOffset? ComputeNextRun(string cronExpression)
    {
        var cron = Cronos.CronExpression.Parse(cronExpression, Cronos.CronFormat.Standard);
        return cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
    }
}
