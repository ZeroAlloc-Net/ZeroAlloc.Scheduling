namespace ZeroAlloc.Scheduling.EfCore;

public sealed class JobEntryEntity
{
    public Guid Id { get; set; }
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

    public JobEntry ToJobEntry() => new()
    {
        Id = new JobId(Id), TypeName = TypeName, Payload = Payload, Status = Status,
        Attempts = Attempts, MaxAttempts = MaxAttempts, ScheduledAt = ScheduledAt,
        StartedAt = StartedAt, CompletedAt = CompletedAt, NextRunAt = NextRunAt,
        CronExpression = CronExpression, Error = Error,
    };
}
