using StackExchange.Redis;

namespace ZeroAlloc.Scheduling.Redis;

public sealed class RedisJobStore : IJobStore, IJobDashboardStore
{
    private readonly IDatabase _db;

    public RedisJobStore(IDatabase db) => _db = db;

    public async ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        var id = JobId.New().Value;
        var score = (double)scheduledAt.ToUnixTimeSeconds();
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id}", BuildHash(id, typeName, payload, JobStatus.Pending, 0, maxAttempts, scheduledAt, cronExpression));
        _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score);
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ids = await _db.SortedSetRangeByScoreAsync("jobs:pending", stop: now, take: batchSize).ConfigureAwait(false);

        var claimed = new List<JobEntry>();
        foreach (var id in ids)
        {
            // Atomically claim: remove from pending sorted set, add to running set, update status
            var result = await _db.ScriptEvaluateAsync(
                "local r=redis.call('ZREM',KEYS[1],ARGV[1]) if r==0 then return nil end redis.call('SADD',KEYS[2],ARGV[1]) redis.call('HSET',KEYS[3],'status','Running','startedAt',ARGV[2]) return 1",
                [(RedisKey)"jobs:pending", (RedisKey)"jobs:running", (RedisKey)$"job:{id}"],
                [(RedisValue)id.ToString(), (RedisValue)DateTimeOffset.UtcNow.ToUnixTimeSeconds()])
                .ConfigureAwait(false);

            if (result.IsNull) continue;

            var entry = await ReadEntryAsync(id.ToString()).ConfigureAwait(false);
            if (entry != null) claimed.Add(entry);
        }
        return claimed;
    }

    public async ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id.Value}", [new HashEntry("status", "Succeeded"), new HashEntry("completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())]);
        _ = tran.SetRemoveAsync("jobs:running", id.Value.ToString());
        _ = tran.SetAddAsync("jobs:succeeded", id.Value.ToString());

        if (nextRunAt.HasValue)
        {
            var typeName = (string?)(await _db.HashGetAsync($"job:{id.Value}", "typeName").ConfigureAwait(false));
            var payload = (byte[]?)(await _db.HashGetAsync($"job:{id.Value}", "payload").ConfigureAwait(false));
            if (typeName != null && payload != null)
            {
                var nextId = JobId.New().Value;
                _ = tran.HashSetAsync($"job:{nextId}", BuildHash(nextId, typeName, payload, JobStatus.Pending, 0, maxAttempts, nextRunAt.Value, cronExpression));
                _ = tran.SortedSetAddAsync("jobs:pending", nextId.ToString(), (double)nextRunAt.Value.ToUnixTimeSeconds());
            }
        }
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        var score = (double)nextRetryAt.ToUnixTimeSeconds();
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id.Value}", [new HashEntry("status", "Failed"), new HashEntry("attempts", attempts), new HashEntry("scheduledAt", score)]);
        _ = tran.SetRemoveAsync("jobs:running", id.Value.ToString());
        _ = tran.SortedSetAddAsync("jobs:pending", id.Value.ToString(), score); // re-add to pending sorted set for retry
        _ = tran.SetAddAsync("jobs:failed", id.Value.ToString());
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id.Value}", [new HashEntry("status", "DeadLetter"), new HashEntry("error", error), new HashEntry("completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())]);
        _ = tran.SetRemoveAsync("jobs:running", id.Value.ToString());
        _ = tran.SetAddAsync("jobs:deadletter", id.Value.ToString());
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        // Guard: check if typename is already in the recurring set (means Pending or Running entry exists)
        var scriptResult = await _db.ScriptEvaluateAsync(
            "return redis.call('SISMEMBER',KEYS[1],ARGV[1]) == 1",
            [(RedisKey)"jobs:recurring"], [(RedisValue)typeName]).ConfigureAwait(false);

        bool exists = (long)scriptResult == 1;

        if (!exists)
        {
            var id = JobId.New().Value;
            var score = (double)scheduledAt.ToUnixTimeSeconds();
            var tran = _db.CreateTransaction();
            _ = tran.HashSetAsync($"job:{id}", BuildHash(id, typeName, payload, JobStatus.Pending, 0, maxAttempts, scheduledAt, cronExpression));
            _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score);
            _ = tran.SetAddAsync("jobs:recurring", typeName);
            await tran.ExecuteAsync().ConfigureAwait(false);
        }
    }

    public async Task<JobSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var pending = (long)await _db.SortedSetLengthAsync("jobs:pending").ConfigureAwait(false);
        var running = (long)await _db.SetLengthAsync("jobs:running").ConfigureAwait(false);
        var succeeded = (long)await _db.SetLengthAsync("jobs:succeeded").ConfigureAwait(false);
        var failed = (long)await _db.SetLengthAsync("jobs:failed").ConfigureAwait(false);
        var deadLetter = (long)await _db.SetLengthAsync("jobs:deadletter").ConfigureAwait(false);
        return new JobSummary((int)pending, (int)running, (int)succeeded, (int)failed, (int)deadLetter);
    }

    public async Task<IReadOnlyList<JobEntry>> QueryByStatusAsync(
        JobStatus[] statuses, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var results = new List<JobEntry>();
        foreach (var status in statuses)
        {
            await AppendEntriesByStatusAsync(results, status, page, pageSize).ConfigureAwait(false);
        }
        return results;
    }

    private async Task AppendEntriesByStatusAsync(List<JobEntry> results, JobStatus status, int page, int pageSize)
    {
        RedisValue[] raw;
        if (status == JobStatus.Pending)
            raw = await _db.SortedSetRangeByRankAsync("jobs:pending", (page - 1) * pageSize, page * pageSize - 1).ConfigureAwait(false);
        else if (status == JobStatus.Running)
            raw = await _db.SetMembersAsync("jobs:running").ConfigureAwait(false);
        else if (status == JobStatus.Succeeded)
            raw = await _db.SetMembersAsync("jobs:succeeded").ConfigureAwait(false);
        else if (status == JobStatus.Failed)
            raw = await _db.SetMembersAsync("jobs:failed").ConfigureAwait(false);
        else if (status == JobStatus.DeadLetter)
            raw = await _db.SetMembersAsync("jobs:deadletter").ConfigureAwait(false);
        else
            return;

        foreach (var v in raw)
        {
            var entry = await ReadEntryAsync(v.ToString()).ConfigureAwait(false);
            if (entry != null) results.Add(entry);
        }
    }

    public async Task<IReadOnlyList<JobEntry>> GetRecurringAsync(CancellationToken ct = default)
    {
        var typeNames = await _db.SetMembersAsync("jobs:recurring").ConfigureAwait(false);
        var results = new List<JobEntry>();
        var pendingIds = await _db.SortedSetRangeByRankAsync("jobs:pending").ConfigureAwait(false);
        foreach (var typeName in typeNames)
        {
            var typeNameStr = typeName.ToString();
            foreach (var idValue in pendingIds)
            {
                var entry = await ReadEntryAsync(idValue.ToString()).ConfigureAwait(false);
                if (entry != null && string.Equals(entry.TypeName, typeNameStr, StringComparison.Ordinal))
                {
                    results.Add(entry);
                    break;
                }
            }
        }
        return results;
    }

    public async Task RequeueAsync(JobId id, CancellationToken ct = default)
    {
        var score = (double)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id.Value}", [new HashEntry("status", "Pending"), new HashEntry("error", RedisValue.EmptyString), new HashEntry("scheduledAt", score)]);
        _ = tran.SetRemoveAsync("jobs:deadletter", id.Value.ToString());
        _ = tran.SortedSetAddAsync("jobs:pending", id.Value.ToString(), score);
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(JobId id, CancellationToken ct = default)
    {
        var tran = _db.CreateTransaction();
        _ = tran.KeyDeleteAsync($"job:{id.Value}");
        _ = tran.SortedSetRemoveAsync("jobs:pending", id.Value.ToString());
        _ = tran.SetRemoveAsync("jobs:running", id.Value.ToString());
        _ = tran.SetRemoveAsync("jobs:succeeded", id.Value.ToString());
        _ = tran.SetRemoveAsync("jobs:failed", id.Value.ToString());
        _ = tran.SetRemoveAsync("jobs:deadletter", id.Value.ToString());
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    private static HashEntry[] BuildHash(Guid id, string typeName, byte[] payload, JobStatus status,
        int attempts, int maxAttempts, DateTimeOffset scheduledAt, string? cronExpression)
    {
        var list = new List<HashEntry>
        {
            new("id", id.ToString()),
            new("typeName", typeName),
            new("payload", payload),
            new("status", status.ToString()),
            new("attempts", attempts),
            new("maxAttempts", maxAttempts),
            new("scheduledAt", scheduledAt.ToUnixTimeSeconds()),
        };
        if (cronExpression != null) list.Add(new HashEntry("cronExpression", cronExpression));
        return [.. list];
    }

    private async Task<JobEntry?> ReadEntryAsync(string id)
    {
        var hash = await _db.HashGetAllAsync($"job:{id}").ConfigureAwait(false);
        if (hash.Length == 0) return null;
        var d = hash.ToDictionary(h => h.Name.ToString(), h => h.Value, StringComparer.Ordinal);
        return BuildJobEntry(d);
    }

    private static JobEntry BuildJobEntry(Dictionary<string, RedisValue> d)
    {
        return new JobEntry
        {
            Id = new JobId(Guid.Parse((string)d["id"]!)),
            TypeName = d["typeName"]!,
            Payload = (byte[])d["payload"]!,
            Status = Enum.Parse<JobStatus>((string)d["status"]!),
            Attempts = (int)d["attempts"],
            MaxAttempts = (int)d["maxAttempts"],
            ScheduledAt = DateTimeOffset.FromUnixTimeSeconds((long)d["scheduledAt"]),
            CronExpression = d.TryGetValue("cronExpression", out var c) ? (string?)c : null,
            Error = d.TryGetValue("error", out var e) ? (string?)e : null,
        };
    }
}
