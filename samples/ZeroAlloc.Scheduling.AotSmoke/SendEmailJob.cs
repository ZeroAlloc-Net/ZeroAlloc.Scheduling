using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.AotSmoke;

// [ZeroAllocSerializable] makes the ZeroAlloc.Serialisation generator emit a serializer for the job
// and include it in AddSerializerDispatcher(), the AOT-safe choice for AddScheduling().
[Job(MaxAttempts = 3)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class SendEmailJob : IJob
{
    public string To { get; init; } = "";

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        ctx.Services.GetRequiredService<DeliverySink>().Delivered.TrySetResult(To);
        return ValueTask.CompletedTask;
    }
}
