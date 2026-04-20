namespace ZeroAlloc.Scheduling;

/// <summary>A background job. Implement this on any class annotated with <see cref="JobAttribute"/>.</summary>
public interface IJob
{
    ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct);
}
