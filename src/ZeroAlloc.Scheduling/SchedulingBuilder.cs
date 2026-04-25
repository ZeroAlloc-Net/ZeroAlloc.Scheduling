using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling;

internal sealed class SchedulingBuilder(IServiceCollection services) : ISchedulingBuilder
{
    public IServiceCollection Services { get; } = services;
}
