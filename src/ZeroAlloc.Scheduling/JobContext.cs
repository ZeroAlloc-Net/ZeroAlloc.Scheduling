namespace ZeroAlloc.Scheduling;

/// <summary>Execution context passed into every job.</summary>
public sealed class JobContext
{
    public required JobId JobId { get; init; }
    public required int Attempt { get; init; }
    public required DateTimeOffset ScheduledAt { get; init; }
    public required IServiceProvider Services { get; init; }
}
