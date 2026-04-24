using ZeroAlloc.Resilience;

namespace ZeroAlloc.Scheduling;

/// <summary>Non-generic executor for a specific job type — looked up by <see cref="TypeName"/> in the worker.</summary>
/// <remarks>
/// <para>
/// <see cref="ExecuteAsync"/> is annotated with <see cref="RetryAttribute"/> so the Resilience
/// generator can emit a proxy for any single-implementation wrapping scenario.
/// </para>
/// <para>
/// <b>Scheduling#18 — Store-level backoff replacement: deferred (semantic mismatch).</b><br/>
/// The hand-rolled exponential backoff in <c>SchedulingWorkerService.ProcessEntryAsync</c>
/// (<c>nextRetry = DateTimeOffset.UtcNow.Add(delay)</c> written to the durable store via
/// <c>IJobStore.MarkFailedAsync</c>) operates at a fundamentally different layer than the
/// in-process retry loop that <c>[Retry]</c> generates. The store-level <c>nextRetry</c>
/// timestamp survives process restarts, load-balancer failover, and competing polling workers;
/// it is a <em>durable cross-poll schedule</em>. The Resilience-generated proxy retries
/// immediately inside the same call stack, within the same polling loop iteration, with no
/// durable state. Replacing the store-level backoff with <c>[Retry]</c> would silently lose
/// durability: a crash mid-retry would discard all in-flight retry state and the job would
/// not be rescheduled. The two mechanisms are complementary: <c>[Retry]</c> handles
/// transient within-attempt faults (network blips, momentary contention); the store-level
/// <c>nextRetry</c> schedule handles cross-attempt durable rescheduling across poll cycles.
/// Both are intentionally retained.
/// </para>
/// </remarks>
public interface IJobTypeExecutor
{
    string TypeName { get; }
    int MaxAttempts { get; }   // 0 = use global default

    /// <summary>Executes the job for the given <paramref name="payload"/> and <paramref name="ctx"/>.</summary>
    /// <remarks>
    /// Annotated with <see cref="RetryAttribute"/> so the Resilience generator produces a proxy
    /// for single-implementation wrappers. Default: 3 attempts, 200 ms exponential backoff.
    /// The <c>IReadOnlyDictionary&lt;string, IJobTypeExecutor&gt;</c> registry in
    /// <c>SchedulingWorkerService</c> is not automatically wrapped — see interface-level remarks.
    /// </remarks>
    [Retry(MaxAttempts = 3, BackoffMs = 200)]
    ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct);
}
