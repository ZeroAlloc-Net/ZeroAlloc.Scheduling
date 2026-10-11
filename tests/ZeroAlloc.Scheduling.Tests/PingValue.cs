using System.Runtime.InteropServices;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// A value-type job, so the enqueue tests cover a payload the serializer must not box.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public readonly record struct PingValue(int Sequence, long Ticks) : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
}
