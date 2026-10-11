using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Mediator;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.AotSmoke;

// A job that is also a ZeroAlloc.Mediator request: the Scheduling generator registers
// MediatorJobTypeExecutor<SendEmailRequest> for it, which hands it to the handler below.
[Job]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class SendEmailRequest : IJob, IRequest<Unit>
{
    public string To { get; init; } = "";

    // Not called on the mediator path; the handler runs instead.
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => ValueTask.CompletedTask;
}
