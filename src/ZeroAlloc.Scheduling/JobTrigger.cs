namespace ZeroAlloc.Scheduling;

/// <summary>
/// Trigger enum for <see cref="JobStatusFsm"/>. Each trigger maps to one logical
/// state-store operation so the FSM encodes the complete valid transition table.
/// </summary>
public enum JobTrigger
{
    /// <summary>Worker claims a Pending or Failed job and begins executing it.</summary>
    Claim,

    /// <summary>Execution succeeded. Job moves to Succeeded (and a new Pending entry may be created for recurring jobs).</summary>
    Succeed,

    /// <summary>Execution failed but retries remain. Job moves back to Failed with a future scheduled-at.</summary>
    Fail,

    /// <summary>All retry attempts exhausted, or an unrecoverable error occurred. Job is dead-lettered.</summary>
    DeadLetter,

    /// <summary>A dead-lettered or succeeded job is manually re-queued by an operator.</summary>
    Requeue,
}
