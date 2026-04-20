using StackExchange.Redis;

namespace ZeroAlloc.Scheduling.Redis;

public sealed class RedisJobStore : IJobStore
{
    private readonly IDatabase _db;

    public RedisJobStore(IDatabase db) => _db = db;

    public async ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        var id = Guid.NewGuid();
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

    public async ValueTask MarkSucceededAsync(Guid id, DateTimeOffset? nextRunAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "Succeeded"), new HashEntry("completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())]);
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());

        if (nextRunAt.HasValue)
        {
            var typeName = (string?)(await _db.HashGetAsync($"job:{id}", "typeName").ConfigureAwait(false));
            var payload = (byte[]?)(await _db.HashGetAsync($"job:{id}", "payload").ConfigureAwait(false));
            if (typeName != null && payload != null)
            {
                var nextId = Guid.NewGuid();
                _ = tran.HashSetAsync($"job:{nextId}", BuildHash(nextId, typeName, payload, JobStatus.Pending, 0, maxAttempts, nextRunAt.Value, cronExpression));
                _ = tran.SortedSetAddAsync("jobs:pending", nextId.ToString(), (double)nextRunAt.Value.ToUnixTimeSeconds());
            }
        }
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask MarkFailedAsync(Guid id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        var score = (double)nextRetryAt.ToUnixTimeSeconds();
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "Failed"), new HashEntry("attempts", attempts), new HashEntry("scheduledAt", score)]);
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());
        _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score); // re-add to pending sorted set for retry
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask DeadLetterAsync(Guid id, string error, CancellationToken ct)
    {
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "DeadLetter"), new HashEntry("error", error), new HashEntry("completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())]);
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());
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
            var id = Guid.NewGuid();
            var score = (double)scheduledAt.ToUnixTimeSeconds();
            var tran = _db.CreateTransaction();
            _ = tran.HashSetAsync($"job:{id}", BuildHash(id, typeName, payload, JobStatus.Pending, 0, maxAttempts, scheduledAt, cronExpression));
            _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score);
            _ = tran.SetAddAsync("jobs:recurring", typeName);
            await tran.ExecuteAsync().ConfigureAwait(false);
        }
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
            Id = Guid.Parse((string)d["id"]!),
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
