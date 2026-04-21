using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Mediator;

public static class MediatorSchedulingExtensions
{
    /// <summary>
    /// Registers the ZeroAlloc.Scheduling mediator bridge.
    /// <para>
    /// NOTE: Automatic source-generator integration is planned but not yet implemented.
    /// Callers must manually register <see cref="MediatorJobTypeExecutor{TJob}"/> for each job type, e.g.:
    /// <code>
    /// services.AddTransient&lt;IJobTypeExecutor, MediatorJobTypeExecutor&lt;MyJob&gt;&gt;();
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddSchedulingMediator(this IServiceCollection services)
    {
        return services;
    }
}
