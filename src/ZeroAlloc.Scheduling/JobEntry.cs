namespace ZeroAlloc.Scheduling;

/// <summary>A persisted job record fetched from the store.</summary>
public sealed class JobEntry
{
    public required Guid Id { get; init; }
    public required string TypeName { get; init; }
    public required byte[] Payload { get; init; }
    public required JobStatus Status { get; init; }
    public required int Attempts { get; init; }
    public required int MaxAttempts { get; init; }
    public required DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public string? CronExpression { get; init; }
    public string? Error { get; init; }
}
