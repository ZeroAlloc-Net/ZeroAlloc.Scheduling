using System.Runtime.InteropServices;
using ZeroAlloc.Mediator;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// A value-type job run through the Mediator bridge. <c>[Job]</c> applies only to classes, so
/// <see cref="Mediator.MediatorJobTypeExecutor{TJob}"/> is how a struct job reaches the worker's read path.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public readonly record struct PingValueRequest(int Sequence, long Ticks) : IJob, IRequest<Unit>
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
}
