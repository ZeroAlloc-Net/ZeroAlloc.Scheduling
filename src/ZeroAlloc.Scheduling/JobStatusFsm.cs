#pragma warning disable ZSM0003 // Succeed, Fail, and DeadLetter are intentionally single-use triggers — each maps to a distinct outcome

using ZeroAlloc.StateMachine;

namespace ZeroAlloc.Scheduling;

/// <summary>
/// Source-generated finite state machine that encodes all valid <see cref="JobStatus"/>
/// transitions. Replaces the duplicated per-store ad-hoc status assignments that
/// previously appeared in <c>EfCoreJobStore</c>, <c>InMemoryJobStore</c>, and
/// <c>RedisJobStore</c>.
/// </summary>
/// <remarks>
/// Usage pattern — create one instance per transition attempt, seed it with the
/// current status via the constructor, then call <c>TryFire</c>:
/// <code>
/// var fsm = new JobStatusFsm(entry.Status);
/// if (!fsm.TryFire(JobTrigger.Claim))
///     throw new InvalidOperationException($"Cannot claim job in state {entry.Status}.");
/// // fsm.Current == JobStatus.Running
/// </code>
/// The class is <c>partial</c>; the ZeroAlloc.StateMachine generator emits
/// <c>TryFire(JobTrigger)</c> and the <c>Current</c> property.
/// <para>
/// On a hot path, such as a store's poll loop, use <see cref="TryTransition"/> instead: it runs
/// the same generated transition table without allocating a machine per call.
/// </para>
/// </remarks>
[StateMachine(InitialState = nameof(JobStatus.Pending))]
// Pending  → Running    (worker claims the job)
[Transition<JobStatus, JobTrigger>(From = JobStatus.Pending,    On = JobTrigger.Claim,      To = JobStatus.Running)]
// Failed   → Running    (retry claim — worker picks up a failed job whose scheduled-at has passed)
[Transition<JobStatus, JobTrigger>(From = JobStatus.Failed,     On = JobTrigger.Claim,      To = JobStatus.Running)]
// Running  → Succeeded  (execution succeeded)
[Transition<JobStatus, JobTrigger>(From = JobStatus.Running,    On = JobTrigger.Succeed,    To = JobStatus.Succeeded)]
// Running  → Failed     (execution failed, retries remain)
[Transition<JobStatus, JobTrigger>(From = JobStatus.Running,    On = JobTrigger.Fail,       To = JobStatus.Failed)]
// Running  → DeadLetter (all retries exhausted)
[Transition<JobStatus, JobTrigger>(From = JobStatus.Running,    On = JobTrigger.DeadLetter, To = JobStatus.DeadLetter)]
// DeadLetter → Pending  (operator requeue)
[Transition<JobStatus, JobTrigger>(From = JobStatus.DeadLetter, On = JobTrigger.Requeue,    To = JobStatus.Pending)]
// Succeeded  → Pending  (operator requeue)
[Transition<JobStatus, JobTrigger>(From = JobStatus.Succeeded,  On = JobTrigger.Requeue,    To = JobStatus.Pending)]
// Sinks: Succeeded and DeadLetter have no spontaneous outgoing transitions; Requeue is operator-driven.
[Terminal<JobStatus>(State = JobStatus.Succeeded)]
[Terminal<JobStatus>(State = JobStatus.DeadLetter)]
public sealed partial class JobStatusFsm
{
    /// <summary>
    /// Initializes a new <see cref="JobStatusFsm"/> seeded with the job's current status.
    /// The generator sets <c>_state</c> to <c>JobStatus.Pending</c> via field initialization;
    /// this constructor overrides that value so the FSM reflects the persisted status.
    /// </summary>
    public JobStatusFsm(JobStatus current)
    {
        _state = current;
    }

    // One machine per thread, re-seeded on every call. The generated hooks are not implemented,
    // so re-seeding with ResetTo and firing has no side effects beyond the state change.
    [ThreadStatic]
    private static JobStatusFsm? t_scratch;

    /// <summary>
    /// Computes the status that <paramref name="trigger"/> moves a job in status
    /// <paramref name="from"/> to, using the generated transition table, without allocating.
    /// </summary>
    /// <param name="from">The job's current status.</param>
    /// <param name="trigger">The trigger to fire.</param>
    /// <param name="to">
    /// The resulting status when the transition is legal; otherwise <paramref name="from"/>.
    /// </param>
    /// <returns><c>true</c> when <paramref name="trigger"/> is legal from <paramref name="from"/>.</returns>
    public static bool TryTransition(JobStatus from, JobTrigger trigger, out JobStatus to)
    {
        var fsm = t_scratch ??= new JobStatusFsm(from);
        fsm.ResetTo(from);
        if (fsm.TryFire(trigger))
        {
            to = fsm.Current;
            return true;
        }

        to = from;
        return false;
    }
}
