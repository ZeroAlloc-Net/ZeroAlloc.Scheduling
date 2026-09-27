using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// A generator-backed job set up for the AOT-safe serializer: the Scheduling generator emits its
/// executor and <c>AddPingJob()</c>, and the ZeroAlloc.Serialisation generator emits a serializer
/// that routes through <see cref="PingJsonContext"/>.
/// </summary>
[Job]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed record Ping(string Message) : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        ctx.Services.GetService(typeof(PingSink)).Should().BeOfType<PingSink>()
            .Which.Received.TrySetResult(Message);
        return ValueTask.CompletedTask;
    }
}
