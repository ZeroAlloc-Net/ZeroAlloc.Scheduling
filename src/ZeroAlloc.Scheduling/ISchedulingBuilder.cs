using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling;

/// <summary>
/// Fluent builder returned by <see cref="SchedulingServiceCollectionExtensions.AddScheduling"/>.
/// Exposes the underlying <see cref="IServiceCollection"/> via <see cref="Services"/>;
/// downstream packages add <c>With*</c> extensions on this interface.
/// </summary>
public interface ISchedulingBuilder
{
    IServiceCollection Services { get; }
}
