using ZeroAlloc.Collections;

namespace ZeroAlloc.Scheduling.InMemory;

/// <summary>Thread-safe in-memory job store. Use in tests — not for production.</summary>
public sealed class InMemoryJobStore : IJobStore, IJobDashboardStore, IDisposable
{
    private readonly ConcurrentHeapSpanDictionary<JobId, JobEntry> _entries = new();

    /// <summary>All entries — for test assertions.</summary>
    public IReadOnlyCollection<JobEntry> AllEntries => _entries.ToValuesArray();

    public ValueTask EnqueueAsync(
        string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        var entry = new JobEntry
        {
            Id = JobId.New(),
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

            // Scheduling#16: use JobStatusFsm to validate that Claim is a legal trigger
            // from this entry's current status. Only Pending and Failed allow Claim.
            var fsm = new JobStatusFsm(e.Status);
            if (!fsm.TryFire(JobTrigger.Claim)) continue;
            if (e.ScheduledAt > now) continue;

            var running = new JobEntry
            {
                Id = e.Id,
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = fsm.Current, // JobStatus.Running — from the FSM
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
        JobId id, DateTimeOffset? nextRunAt, string? cronExpression,
        int maxAttempts, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;

        // Scheduling#16: validate Running → Succeeded via FSM
        var fsm = new JobStatusFsm(e.Status);
        if (!fsm.TryFire(JobTrigger.Succeed)) return ValueTask.CompletedTask;

        _entries[id] = new JobEntry
        {
            Id = e.Id,
            TypeName = e.TypeName,
            Payload = e.Payload,
            Status = fsm.Current, // JobStatus.Succeeded
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
                Id = JobId.New(),
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

    public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;

        // Scheduling#16: validate Running → Failed via FSM
        var fsm = new JobStatusFsm(e.Status);
        if (!fsm.TryFire(JobTrigger.Fail)) return ValueTask.CompletedTask;

        _entries[id] = new JobEntry
        {
            Id = e.Id,
            TypeName = e.TypeName,
            Payload = e.Payload,
            Status = fsm.Current, // JobStatus.Failed
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

    public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;

        // Scheduling#16: validate Running → DeadLetter via FSM
        var fsm = new JobStatusFsm(e.Status);
        if (!fsm.TryFire(JobTrigger.DeadLetter)) return ValueTask.CompletedTask;

        _entries[id] = new JobEntry
        {
            Id = e.Id,
            TypeName = e.TypeName,
            Payload = e.Payload,
            Status = fsm.Current, // JobStatus.DeadLetter
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
                Id = JobId.New(),
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

    public Task RequeueAsync(JobId id, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(id, out var e))
        {
            // Scheduling#16: validate DeadLetter/Succeeded → Pending via FSM
            var fsm = new JobStatusFsm(e.Status);
            if (fsm.TryFire(JobTrigger.Requeue))
            {
                _entries[id] = new JobEntry
                {
                    Id = e.Id,
                    TypeName = e.TypeName,
                    Payload = e.Payload,
                    Status = fsm.Current, // JobStatus.Pending
                    Attempts = 0,
                    MaxAttempts = e.MaxAttempts,
                    ScheduledAt = DateTimeOffset.UtcNow,
                    CronExpression = e.CronExpression,
                };
            }
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(JobId id, CancellationToken ct = default)
    {
        _entries.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <summary>Returns the pooled bucket array. Tests typically rely on GC; production callers should dispose explicitly.</summary>
    public void Dispose() => _entries.Dispose();
}
