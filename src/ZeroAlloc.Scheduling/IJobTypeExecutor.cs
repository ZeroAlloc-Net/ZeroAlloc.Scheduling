namespace ZeroAlloc.Scheduling;

/// <summary>Non-generic executor for a specific job type — looked up by <see cref="TypeName"/> in the worker.</summary>
public interface IJobTypeExecutor
{
    string TypeName { get; }
    int MaxAttempts { get; }   // 0 = use global default
    ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct);
}
