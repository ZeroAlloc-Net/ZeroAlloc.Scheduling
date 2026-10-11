using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Scheduling.AotSmoke;

// Completes the delivery for SendEmailRequest, which runs through MediatorJobTypeExecutor.
internal sealed class SendEmailRequestHandler(DeliverySink sink) : IRequestHandler<SendEmailRequest, Unit>
{
    public ValueTask<Unit> Handle(SendEmailRequest request, CancellationToken ct)
    {
        sink.Delivered.TrySetResult(request.To);
        return ValueTask.FromResult(Unit.Value);
    }
}
