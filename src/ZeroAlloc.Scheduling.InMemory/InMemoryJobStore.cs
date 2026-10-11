using ZeroAlloc.Collections;

namespace ZeroAlloc.Scheduling.InMemory;

/// <summary>Thread-safe in-memory job store. Use in tests — not for production.</summary>
/// <remarks>
/// Finished jobs, <see cref="JobStatus.Succeeded"/> and <see cref="JobStatus.DeadLetter"/>, stay in
/// the store so the dashboard can count, list and requeue them, as they do in the EF Core, ORM and
/// Redis stores; <see cref="DeleteAsync"/> removes one. Only Pending and Failed jobs are in the
/// claimable index that <see cref="FetchPendingAsync"/> scans, so the cost of a poll depends on the
/// number of waiting jobs, not on how many jobs have ever run.
/// </remarks>
public sealed class InMemoryJobStore : IJobStore, IJobDashboardStore, IDisposable
{
    private readonly ConcurrentHeapSpanDictionary<JobId, JobEntry> _entries = new();

    // Ids of the Pending and Failed entries: the only ones a poll can claim. Every write that
    // changes an entry's status happens under _gate together with the matching change here,
    // so the two collections never disagree.
    private readonly HashSet<JobId> _claimable = [];
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif

    /// <summary>All entries — for test assertions.</summary>
    public IReadOnlyCollection<JobEntry> AllEntries => _entries.ToValuesArray();

    public ValueTask EnqueueAsync(
        string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        var entry = NewPending(typeName, payload, scheduledAt, maxAttempts, cronExpression);
        lock (_gate)
        {
            _entries[entry.Id] = entry;
            _claimable.Add(entry.Id);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        List<JobEntry>? claimed = null;

        lock (_gate)
        {
            foreach (var id in _claimable)
            {
                if (claimed?.Count >= batchSize) break;
                if (!_entries.TryGetValue(id, out var e)) continue;
                if (e.ScheduledAt > now) continue;

                // Scheduling#16: the FSM decides whether Claim is legal from this status.
                // Only Pending and Failed allow it.
                if (!JobStatusFsm.TryTransition(e.Status, JobTrigger.Claim, out var status)) continue;

                var running = new JobEntry
                {
                    Id = e.Id,
                    TypeName = e.TypeName,
                    Payload = e.Payload,
                    Status = status, // JobStatus.Running
                    Attempts = e.Attempts,
                    MaxAttempts = e.MaxAttempts,
                    ScheduledAt = e.ScheduledAt,
                    StartedAt = now,
                    CompletedAt = e.CompletedAt,
                    NextRunAt = e.NextRunAt,
                    CronExpression = e.CronExpression,
                    Error = e.Error,
                };
                _entries[id] = running;
                (claimed ??= new List<JobEntry>(Math.Min(batchSize, _claimable.Count))).Add(running);
            }

            if (claimed is null)
                return new ValueTask<IReadOnlyList<JobEntry>>(Array.Empty<JobEntry>());

            foreach (var e in claimed)
                _claimable.Remove(e.Id);
        }

        return new ValueTask<IReadOnlyList<JobEntry>>(claimed);
    }

    public ValueTask MarkSucceededAsync(
        JobId id, DateTimeOffset? nextRunAt, string? cronExpression,
        int maxAttempts, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;

            // Scheduling#16: validate Running → Succeeded via FSM
            if (!JobStatusFsm.TryTransition(e.Status, JobTrigger.Succeed, out var status))
                return ValueTask.CompletedTask;

            _entries[id] = new JobEntry
            {
                Id = e.Id,
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = status, // JobStatus.Succeeded
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
                var next = NewPending(e.TypeName, e.Payload, nextRunAt.Value, maxAttempts, cronExpression);
                _entries[next.Id] = next;
                _claimable.Add(next.Id);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;

            // Scheduling#16: validate Running → Failed via FSM
            if (!JobStatusFsm.TryTransition(e.Status, JobTrigger.Fail, out var status))
                return ValueTask.CompletedTask;

            _entries[id] = new JobEntry
            {
                Id = e.Id,
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = status, // JobStatus.Failed
                Attempts = attempts,
                MaxAttempts = e.MaxAttempts,
                ScheduledAt = nextRetryAt,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
                NextRunAt = e.NextRunAt,
                CronExpression = e.CronExpression,
                Error = e.Error,
            };
            _claimable.Add(id); // claimed again once nextRetryAt has passed
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var e)) return ValueTask.CompletedTask;

            // Scheduling#16: validate Running → DeadLetter via FSM
            if (!JobStatusFsm.TryTransition(e.Status, JobTrigger.DeadLetter, out var status))
                return ValueTask.CompletedTask;

            _entries[id] = new JobEntry
            {
                Id = e.Id,
                TypeName = e.TypeName,
                Payload = e.Payload,
                Status = status, // JobStatus.DeadLetter
                Attempts = e.Attempts,
                MaxAttempts = e.MaxAttempts,
                ScheduledAt = e.ScheduledAt,
                StartedAt = e.StartedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                NextRunAt = e.NextRunAt,
                CronExpression = e.CronExpression,
                Error = error,
            };
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask UpsertRecurringAsync(
        string typeName, byte[] payload, DateTimeOffset scheduledAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        lock (_gate)
        {
            bool exists = _entries.ToValuesArray().Any(e =>
                string.Equals(e.TypeName, typeName, StringComparison.Ordinal) &&
                (e.Status == JobStatus.Pending || e.Status == JobStatus.Running));

            if (!exists)
            {
                var entry = NewPending(typeName, payload, scheduledAt, maxAttempts, cronExpression);
                _entries[entry.Id] = entry;
                _claimable.Add(entry.Id);
            }
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
        lock (_gate)
        {
            // Scheduling#16: validate DeadLetter/Succeeded → Pending via FSM
            if (_entries.TryGetValue(id, out var e)
                && JobStatusFsm.TryTransition(e.Status, JobTrigger.Requeue, out var status))
            {
                _entries[id] = new JobEntry
                {
                    Id = e.Id,
                    TypeName = e.TypeName,
                    Payload = e.Payload,
                    Status = status, // JobStatus.Pending
                    Attempts = 0,
                    MaxAttempts = e.MaxAttempts,
                    ScheduledAt = DateTimeOffset.UtcNow,
                    CronExpression = e.CronExpression,
                };
                _claimable.Add(id);
            }
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(JobId id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _entries.TryRemove(id, out _);
            _claimable.Remove(id);
        }
        return Task.CompletedTask;
    }

    /// <summary>Returns the pooled bucket array. Tests typically rely on GC; production callers should dispose explicitly.</summary>
    public void Dispose() => _entries.Dispose();

    private static JobEntry NewPending(
        string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression)
        => new()
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
}
