using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Scheduling.InMemory;

public static class InMemorySchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory <see cref="IJobStore"/> against the scheduling builder.
    /// </summary>
    public static ISchedulingBuilder WithInMemoryStore(this ISchedulingBuilder builder)
    {
        builder.Services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        return builder;
    }
}
