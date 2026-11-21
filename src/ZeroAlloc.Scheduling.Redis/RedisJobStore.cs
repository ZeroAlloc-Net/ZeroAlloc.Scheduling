using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ZeroAlloc.Scheduling.Redis;

/// <summary>
/// <see cref="IJobStore"/> and <see cref="IJobDashboardStore"/> backed by Redis.
/// </summary>
/// <remarks>
/// A poll also takes back a job that has been Running for longer than
/// <see cref="SchedulingOptions.ClaimTimeout"/>. Each claim writes a token to the job's hash, and an
/// outcome this instance records for a job it claimed, success, failure or dead-letter, applies only
/// while the hash still holds that token. A run that outlived its claim therefore cannot overwrite the
/// run that took it back. An outcome for a job this instance did not claim is recorded unchecked.
/// Workers in one process share this singleton, and the claim that took the job back replaces the
/// one remembered for it, so there a late outcome is recorded too: the guard covers a run on another
/// host.
/// </remarks>
public sealed class RedisJobStore : IJobStore, IJobDashboardStore
{
    private static readonly TimeSpan DefaultClaimTimeout = new SchedulingOptions().ClaimTimeout;

    // jobs:claims indexes the Running jobs by the start of their claim, in Unix milliseconds, so a
    // poll finds the expired claims with one bounded range query.
    private const string ClaimsKey = "jobs:claims";

    private const string ClaimScript =
        "local r=redis.call('ZREM',KEYS[1],ARGV[1]) if r==0 then return nil end redis.call('SADD',KEYS[2],ARGV[1]) redis.call('HSET',KEYS[3],'status','Running','startedAt',ARGV[2],'claim',ARGV[3]) redis.call('ZADD',KEYS[4],ARGV[4],ARGV[1]) return 1";

    // Takes back a Running job whose claim started at or before ARGV[1], counting it as an attempt.
    // The status and start are checked inside the script, so of two pollers only one takes it. An
    // index entry for a job that is no longer Running is stale, left by a release that did not keep
    // the index, and is dropped so it cannot hold a place in every later poll.
    private const string ReclaimScript =
        "if redis.call('HGET',KEYS[1],'status')~='Running' then redis.call('ZREM',KEYS[2],ARGV[4]) return nil end local t=tonumber(redis.call('ZSCORE',KEYS[2],ARGV[4])) if t==nil or t>tonumber(ARGV[1]) then return nil end redis.call('HSET',KEYS[1],'startedAt',ARGV[2],'claim',ARGV[3]) redis.call('HINCRBY',KEYS[1],'attempts',1) redis.call('ZADD',KEYS[2],ARGV[5],ARGV[4]) return 1";

    // Adds Running jobs claimed by a release without jobs:claims to that index, once per instance.
    // Their start is stored in whole seconds, so the next second is used: a claim is never taken
    // back early.
    private const string BackfillScript =
        "for _,id in ipairs(redis.call('SMEMBERS',KEYS[1])) do if not redis.call('ZSCORE',KEYS[2],id) then local t=tonumber(redis.call('HGET','job:'..id,'startedAt')) if t then redis.call('ZADD',KEYS[2],(t+1)*1000,id) else redis.call('ZADD',KEYS[2],ARGV[1],id) end end end return 1";

    // Inserts a recurring job's run unless its type name is in jobs:recurring, in one step, so of
    // concurrent callers only one inserts and a change to another type cannot abort the insert.
    private const string UpsertRecurringScript =
        "if redis.call('SISMEMBER',KEYS[1],ARGV[1])==1 then return 0 end redis.call('HSET',KEYS[2],unpack(ARGV,4)) redis.call('ZADD',KEYS[3],ARGV[2],ARGV[3]) redis.call('SADD',KEYS[1],ARGV[1]) return 1";

    private int _backfilled;

    private readonly IDatabase _db;
    private readonly IOptionsMonitor<SchedulingOptions>? _options;

    // The claim token each job this instance claimed was claimed under, removed once its outcome
    // write has returned.
    private readonly ConcurrentDictionary<JobId, string> _claims = new();

    /// <summary>
    /// Creates a store that takes back a Running job after the default
    /// <see cref="SchedulingOptions.ClaimTimeout"/>.
    /// </summary>
    /// <param name="db">The Redis database the jobs live in.</param>
    public RedisJobStore(IDatabase db) => _db = db;

    /// <summary>
    /// Creates a store that takes back a Running job after the configured
    /// <see cref="SchedulingOptions.ClaimTimeout"/>.
    /// </summary>
    /// <param name="db">The Redis database the jobs live in.</param>
    /// <param name="options">The scheduling options; read on every poll.</param>
    public RedisJobStore(IDatabase db, IOptionsMonitor<SchedulingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _options = options;
    }

    public async ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        int maxAttempts, string? cronExpression, CancellationToken ct)
    {
        var id = JobId.New();
        var score = (double)scheduledAt.ToUnixTimeSeconds();
        var tran = _db.CreateTransaction();
        _ = tran.HashSetAsync($"job:{id}", BuildHash(id, typeName, payload, JobStatus.Pending, 0, maxAttempts, scheduledAt, cronExpression));
        _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score);
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var nowOffset = DateTimeOffset.UtcNow;
        var now = nowOffset.ToUnixTimeSeconds();
        var nowMs = nowOffset.ToUnixTimeMilliseconds();
        // In doubles, so a timeout as long as TimeSpan.MaxValue cannot overflow.
        var staleBeforeMs = nowMs - (_options?.CurrentValue.ClaimTimeout ?? DefaultClaimTimeout).TotalMilliseconds;
        var claimToken = Guid.NewGuid().ToString("N");
        var claimed = new List<JobEntry>();

        // Marked done only once it has run, so a failed attempt is repeated by a later poll. Running
        // it twice is harmless: the script leaves indexed jobs alone.
        if (Volatile.Read(ref _backfilled) == 0)
        {
            await _db.ScriptEvaluateAsync(
                BackfillScript, [(RedisKey)"jobs:running", (RedisKey)ClaimsKey], [(RedisValue)nowMs])
                .ConfigureAwait(false);
            Volatile.Write(ref _backfilled, 1);
        }

        // A Running job whose claim has expired was abandoned by a run that recorded no outcome.
        // Expired claims go first: they are overdue, and a queue that always has due jobs would
        // otherwise never reach them.
        var expired = await _db.SortedSetRangeByScoreAsync(ClaimsKey, stop: staleBeforeMs, take: batchSize).ConfigureAwait(false);
        foreach (var id in expired)
        {
            var result = await _db.ScriptEvaluateAsync(
                ReclaimScript,
                [(RedisKey)$"job:{id}", (RedisKey)ClaimsKey],
                [(RedisValue)staleBeforeMs, (RedisValue)now, (RedisValue)claimToken, id, (RedisValue)nowMs])
                .ConfigureAwait(false);

            if (result.IsNull) continue;

            await AddClaimedAsync(claimed, id.ToString(), claimToken).ConfigureAwait(false);
        }

        if (claimed.Count >= batchSize)
            return claimed;

        var ids = await _db.SortedSetRangeByScoreAsync("jobs:pending", stop: now, take: batchSize - claimed.Count).ConfigureAwait(false);
        foreach (var id in ids)
        {
            // Atomically claim: remove from pending sorted set, add to running set, update status
            var result = await _db.ScriptEvaluateAsync(
                ClaimScript,
                [(RedisKey)"jobs:pending", (RedisKey)"jobs:running", (RedisKey)$"job:{id}", (RedisKey)ClaimsKey],
                [(RedisValue)id.ToString(), (RedisValue)now, (RedisValue)claimToken, (RedisValue)nowMs])
                .ConfigureAwait(false);

            if (result.IsNull) continue;

            await AddClaimedAsync(claimed, id.ToString(), claimToken).ConfigureAwait(false);
        }

        return claimed;
    }

    private async Task AddClaimedAsync(List<JobEntry> claimed, string id, string claimToken)
    {
        var entry = await ReadEntryAsync(id).ConfigureAwait(false);
        if (entry is null) return;

        _claims[entry.Id] = claimToken;
        claimed.Add(entry);
    }

    // A transaction for an outcome write: when this instance claimed the job, it executes only while
    // the job is Running under that claim, so a run that outlived its claim writes nothing, and a
    // second outcome for the same claim changes nothing.
    private ITransaction CreateOutcomeTransaction(JobId id)
    {
        var tran = _db.CreateTransaction();
        if (_claims.TryGetValue(id, out var claimToken))
        {
            tran.AddCondition(Condition.HashEqual($"job:{id}", "claim", claimToken));
            tran.AddCondition(Condition.HashEqual($"job:{id}", "status", "Running"));
        }

        _ = tran.SortedSetRemoveAsync(ClaimsKey, id.ToString());
        return tran;
    }

    // Executes an outcome transaction, then releases the claim. Should the write throw, the claim is
    // kept, so the worker's failure write still carries it.
    private async Task ExecuteOutcomeAsync(ITransaction tran, JobId id)
    {
        await tran.ExecuteAsync().ConfigureAwait(false);
        _claims.TryRemove(id, out _);
    }

    public async ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        var tran = CreateOutcomeTransaction(id);
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "Succeeded"), new HashEntry("completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())]);
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());
        _ = tran.SetAddAsync("jobs:succeeded", id.ToString());

        if (nextRunAt.HasValue)
        {
            var typeName = (string?)(await _db.HashGetAsync($"job:{id}", "typeName").ConfigureAwait(false));
            var payload = (byte[]?)(await _db.HashGetAsync($"job:{id}", "payload").ConfigureAwait(false));
            if (typeName != null && payload != null)
            {
                var nextId = JobId.New();
                _ = tran.HashSetAsync($"job:{nextId}", BuildHash(nextId, typeName, payload, JobStatus.Pending, 0, maxAttempts, nextRunAt.Value, cronExpression));
                _ = tran.SortedSetAddAsync("jobs:pending", nextId.ToString(), (double)nextRunAt.Value.ToUnixTimeSeconds());
            }
        }
        await ExecuteOutcomeAsync(tran, id).ConfigureAwait(false);
    }

    public async ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
    {
        var score = (double)nextRetryAt.ToUnixTimeSeconds();
        var tran = CreateOutcomeTransaction(id);
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "Failed"), new HashEntry("attempts", attempts), new HashEntry("scheduledAt", score)]);
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());
        _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score); // re-add to pending sorted set for retry
        _ = tran.SetAddAsync("jobs:failed", id.ToString());
        await ExecuteOutcomeAsync(tran, id).ConfigureAwait(false);
    }

    public async ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
    {
        var fields = await _db.HashGetAsync($"job:{id}", [(RedisValue)"typeName", (RedisValue)"cronExpression"]).ConfigureAwait(false);

        var tran = CreateOutcomeTransaction(id);
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "DeadLetter"), new HashEntry("error", error), new HashEntry("completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())]);
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());
        _ = tran.SetAddAsync("jobs:deadletter", id.ToString());

        // jobs:recurring marks a recurring job that has a live run. A dead-lettered occurrence
        // leaves none, so the worker's UpsertRecurringAsync can add the next occurrence.
        if (!fields[0].IsNull && !fields[1].IsNull)
            _ = tran.SetRemoveAsync("jobs:recurring", fields[0]);

        await ExecuteOutcomeAsync(tran, id).ConfigureAwait(false);
    }

    public async ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt,
        string? cronExpression, int maxAttempts, CancellationToken ct)
    {
        // Guard: the type name is in the recurring set while the job has a live run. The script
        // checks and inserts in one step, so concurrent callers add a single run.
        var id = JobId.New();
        var hash = BuildHash(id, typeName, payload, JobStatus.Pending, 0, maxAttempts, scheduledAt, cronExpression);
        var args = new RedisValue[3 + (hash.Length * 2)];
        args[0] = typeName;
        args[1] = (double)scheduledAt.ToUnixTimeSeconds();
        args[2] = id.ToString();
        for (var i = 0; i < hash.Length; i++)
        {
            args[3 + (i * 2)] = hash[i].Name;
            args[4 + (i * 2)] = hash[i].Value;
        }

        await _db.ScriptEvaluateAsync(
            UpsertRecurringScript,
            [(RedisKey)"jobs:recurring", (RedisKey)$"job:{id}", (RedisKey)"jobs:pending"],
            args).ConfigureAwait(false);
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
        _ = tran.HashSetAsync($"job:{id}", [new HashEntry("status", "Pending"), new HashEntry("attempts", 0), new HashEntry("error", RedisValue.EmptyString), new HashEntry("scheduledAt", score)]);
        _ = tran.SetRemoveAsync("jobs:deadletter", id.ToString());
        _ = tran.SortedSetAddAsync("jobs:pending", id.ToString(), score);
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(JobId id, CancellationToken ct = default)
    {
        var tran = _db.CreateTransaction();
        _ = tran.KeyDeleteAsync($"job:{id}");
        _ = tran.SortedSetRemoveAsync("jobs:pending", id.ToString());
        _ = tran.SetRemoveAsync("jobs:running", id.ToString());
        _ = tran.SortedSetRemoveAsync(ClaimsKey, id.ToString());
        _ = tran.SetRemoveAsync("jobs:succeeded", id.ToString());
        _ = tran.SetRemoveAsync("jobs:failed", id.ToString());
        _ = tran.SetRemoveAsync("jobs:deadletter", id.ToString());
        await tran.ExecuteAsync().ConfigureAwait(false);
    }

    private static HashEntry[] BuildHash(JobId id, string typeName, byte[] payload, JobStatus status,
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
            Id = JobId.Parse((string)d["id"]!),
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
