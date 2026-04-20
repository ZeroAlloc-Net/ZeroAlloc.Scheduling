using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Scheduling.EfCore;

public static class EfCoreSchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingEfCore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<SchedulingDbContext>(configure);
        services.TryAddScoped<IJobStore, EfCoreJobStore>();
        return services;
    }
}
