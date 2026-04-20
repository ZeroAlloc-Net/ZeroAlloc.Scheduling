using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Mediator;

public static class MediatorSchedulingExtensions
{
    /// <summary>
    /// Registers the ZeroAlloc.Scheduling mediator bridge.
    /// Per-type wiring is emitted by the source generator when it detects <c>IRequest&lt;Unit&gt;</c>
    /// on a job class — this method is the DI hook consumed by that generated code.
    /// </summary>
    public static IServiceCollection AddSchedulingMediator(this IServiceCollection services)
    {
        // Generator-emitted Add{TypeName}Job() calls will register MediatorJobTypeExecutor<T>
        // when AddSchedulingMediator() is in scope. This method is the DI anchor.
        return services;
    }
}
