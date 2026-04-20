using System.Collections.Concurrent;

namespace ZeroAlloc.Scheduling.InMemory;

/// <summary>Thread-safe in-memory job store. Use in tests — not for production.</summary>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobEntry> _entries = new();

    /// <summary>All entries — for test assertions.</summary>
    public IReadOnlyCollection<JobEntry> AllEntries => _entries.Values.ToList();

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
        bool exists = _entries.Values.Any(e =>
            string.Equals(e.TypeName, typeName, StringComparison.Ordinal) &&
            e.Status == JobStatus.Pending);

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
}
