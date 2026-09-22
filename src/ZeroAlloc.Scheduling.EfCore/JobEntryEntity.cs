using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.EfCore;

public sealed class JobEntryEntity
{
    public JobId Id { get; set; } = JobId.New();
    public required string TypeName { get; set; }
    public required byte[] Payload { get; set; }
    public JobStatus Status { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public string? CronExpression { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// Identifies which poll claimed this row, so a poller can read back its own
    /// claim and nothing else.
    /// </summary>
    /// <remarks>
    /// Without it the read-back can only ask for "candidate and now Running",
    /// which matches rows claimed by any poller, so two pollers selecting
    /// concurrently are each handed the other's jobs as well as their own.
    /// Null on rows that have never been claimed.
    /// </remarks>
    public Guid? ClaimToken { get; set; }

    public JobEntry ToJobEntry() => new()
    {
        Id = Id, TypeName = TypeName, Payload = Payload, Status = Status,
        Attempts = Attempts, MaxAttempts = MaxAttempts, ScheduledAt = ScheduledAt,
        StartedAt = StartedAt, CompletedAt = CompletedAt, NextRunAt = NextRunAt,
        CronExpression = CronExpression, Error = Error,
    };
}
