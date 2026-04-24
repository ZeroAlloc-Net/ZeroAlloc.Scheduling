using ZeroAlloc.Collections;

namespace ZeroAlloc.Scheduling.InMemory;

/// <summary>Thread-safe in-memory job store. Use in tests — not for production.</summary>
public sealed class InMemoryJobStore : IJobStore, IJobDashboardStore, IDisposable
{
    private readonly ConcurrentHeapSpanDictionary<Guid, JobEntry> _entries = new();

    /// <summary>All entries — for test assertions.</summary>
    public IReadOnlyCollection<JobEntry> AllEntries => _entries.ToValuesArray();

    public ValueTask EnqueueAsync(
        string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        var entry = new JobEntry
        {
            Id = Guid.NewGuid(),
            TypeName = typeName,
            Payload = payload,
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = maxAttempts,
            ScheduledAt = scheduledAt,
            CronExpression = cronExpression,
        };
        _entries[entry.Id] = entry;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var claimed = new List<JobEntry>();

        foreach (var kvp in _entries)
        {
            if (claimed.Count >= batchSize) break;
            var e = kvp.Value;
            bool isRetryable = e.Status == JobStatus.Pending || e.Status == JobStatus.Failed;
            if (!isRetryable || e.ScheduledAt > now) continue;

            var running = new JobEntry
            {
                Id = e.Id,
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = JobStatus.Running,
                Attempts = e.Attempts,
                MaxAttempts = e.MaxAttempts,
                ScheduledAt = e.ScheduledAt,
                StartedAt = now,
                CompletedAt = e.CompletedAt,
                NextRunAt = e.NextRunAt,
                CronExpression = e.CronExpression,
                Error = e.Error,
            };
            if (_entries.TryUpdate(kvp.Key, running, e))
                claimed.Add(running);
        }

        return new ValueTask<IReadOnlyList<JobEntry>>(claimed);
    }

    public ValueTask MarkSucceededAsync(
        Guid id, DateTimeOffset? nextRunAt, string? cronExpression,
        int maxAttempts, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;
        _entries[id] = new JobEntry
        {
            Id = e.Id,
            TypeName = e.TypeName,
            Payload = e.Payload,
            Status = JobStatus.Succeeded,
            Attempts = e.Attempts,
            MaxAttempts = e.MaxAttempts,
            ScheduledAt = e.ScheduledAt,
            StartedAt = e.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            NextRunAt = e.NextRunAt,
            CronExpression = e.CronExpression,
            Error = e.Error,
        };

        if (nextRunAt.HasValue)
        {
            // Recurring: insert next pending entry
            var next = new JobEntry
            {
                Id = Guid.NewGuid(),
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = JobStatus.Pending,
                Attempts = 0,
                MaxAttempts = maxAttempts,
                ScheduledAt = nextRunAt.Value,
                CronExpression = cronExpression,
            };
            _entries[next.Id] = next;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(Guid id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;
        _entries[id] = new JobEntry
        {
            Id = e.Id,
            TypeName = e.TypeName,
            Payload = e.Payload,
            Status = JobStatus.Failed,
            Attempts = attempts,
            MaxAttempts = e.MaxAttempts,
            ScheduledAt = nextRetryAt,
            StartedAt = e.StartedAt,
            CompletedAt = e.CompletedAt,
            NextRunAt = e.NextRunAt,
            CronExpression = e.CronExpression,
            Error = e.Error,
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask DeadLetterAsync(Guid id, string error, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;
        _entries[id] = new JobEntry
        {
            Id = e.Id,
            TypeName = e.TypeName,
            Payload = e.Payload,
            Status = JobStatus.DeadLetter,
            Attempts = e.Attempts,
            MaxAttempts = e.MaxAttempts,
            ScheduledAt = e.ScheduledAt,
            StartedAt = e.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            NextRunAt = e.NextRunAt,
            CronExpression = e.CronExpression,
            Error = error,
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask UpsertRecurringAsync(
        string typeName, byte[] payload, DateTimeOffset scheduledAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        bool exists = _entries.ToValuesArray().Any(e =>
            string.Equals(e.TypeName, typeName, StringComparison.Ordinal) &&
            (e.Status == JobStatus.Pending || e.Status == JobStatus.Running));

        if (!exists)
        {
            var entry = new JobEntry
            {
                Id = Guid.NewGuid(),
                TypeName = typeName,
                Payload = payload,
                Status = JobStatus.Pending,
                Attempts = 0,
                MaxAttempts = maxAttempts,
                ScheduledAt = scheduledAt,
                CronExpression = cronExpression,
            };
            _entries[entry.Id] = entry;
        }

        return ValueTask.CompletedTask;
    }

    // IJobDashboardStore

    public Task<JobSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var counts = _entries.ToValuesArray()
            .GroupBy(e => e.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(new JobSummary(
            counts.GetValueOrDefault(JobStatus.Pending),
            counts.GetValueOrDefault(JobStatus.Running),
            counts.GetValueOrDefault(JobStatus.Succeeded),
            counts.GetValueOrDefault(JobStatus.Failed),
            counts.GetValueOrDefault(JobStatus.DeadLetter)));
    }

    public Task<IReadOnlyList<JobEntry>> QueryByStatusAsync(
        JobStatus[] statuses, int page = 1, int pageSize = 50, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobEntry>>(_entries.ToValuesArray()
            .Where(e => statuses.Contains(e.Status))
            .OrderByDescending(e => e.ScheduledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList());

    public Task<IReadOnlyList<JobEntry>> GetRecurringAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobEntry>>(_entries.ToValuesArray()
            .Where(e => e.CronExpression != null && e.Status == JobStatus.Pending)
            .ToList());

    public Task RequeueAsync(Guid id, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(id, out var e))
        {
            _entries[id] = new JobEntry
            {
                Id = e.Id,
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = JobStatus.Pending,
                Attempts = 0,
                MaxAttempts = e.MaxAttempts,
                ScheduledAt = DateTimeOffset.UtcNow,
                CronExpression = e.CronExpression,
            };
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _entries.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <summary>Returns the pooled bucket array. Tests typically rely on GC; production callers should dispose explicitly.</summary>
    public void Dispose() => _entries.Dispose();
}
