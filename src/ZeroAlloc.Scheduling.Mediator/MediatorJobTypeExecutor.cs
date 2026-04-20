using System.Diagnostics.CodeAnalysis;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Scheduling.Mediator;

/// <summary>
/// Executes a job by deserializing it as an <see cref="IRequest{TResponse}"/> and dispatching via
/// <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// </summary>
[RequiresUnreferencedCode("Uses reflection-based JSON serialization via IJobSerializer")]
[RequiresDynamicCode("Uses reflection-based JSON serialization via IJobSerializer")]
internal sealed class MediatorJobTypeExecutor<TJob> : IJobTypeExecutor
    where TJob : IJob, IRequest<Unit>
{
    private readonly IJobSerializer _serializer;
    private readonly IRequestHandler<TJob, Unit> _handler;

    public MediatorJobTypeExecutor(IJobSerializer serializer, IRequestHandler<TJob, Unit> handler)
    {
        _serializer = serializer;
        _handler = handler;
    }

    public string TypeName => typeof(TJob).FullName!;

    public async ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
    {
        var job = _serializer.Deserialize<TJob>(payload);
        await _handler.Handle(job, ct).ConfigureAwait(false);
    }
}
