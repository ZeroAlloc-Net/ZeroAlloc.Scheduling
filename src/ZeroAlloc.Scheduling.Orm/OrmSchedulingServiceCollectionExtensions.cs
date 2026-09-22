using System.Data.Async;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// Registers ZeroAlloc.ORM as the job store.
/// </summary>
public static class OrmSchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Persists jobs through ZeroAlloc.ORM, against the
    /// <see cref="IAsyncDbConnection"/> already registered in the container.
    /// </summary>
    /// <param name="builder">The scheduling builder.</param>
    /// <param name="dialect">
    /// Which database the registered connection talks to. Only the two bounded
    /// queries differ between providers; everything else is plain ANSI. Defaults
    /// to SQLite, which shares its spelling with PostgreSQL.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Register a scoped <see cref="IAsyncDbConnection"/> yourself — this method
    /// deliberately does not, because the connection's lifetime, provider and
    /// connection string belong to the application, not to the scheduler.
    /// </para>
    /// <para>
    /// Create the schema with <see cref="SchedulingOrmMigrations"/> and the
    /// ORM's <c>MigrationRunner</c>; the store does not create tables on the
    /// fly.
    /// </para>
    /// <para>
    /// No <c>IJobDashboardStore</c> is registered. That interface is a much
    /// larger surface this adapter does not implement, and registering a
    /// non-functional one would leave the dashboard silently empty rather than
    /// failing where the mistake was made.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddScheduling().WithOrm();
    /// services.AddScheduling().WithOrm(OrmSchedulingDialect.SqlServer);
    /// </code>
    /// </example>
    public static ISchedulingBuilder WithOrm(
        this ISchedulingBuilder builder,
        OrmSchedulingDialect dialect = OrmSchedulingDialect.Sqlite)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<IJobStore>(sp =>
            new OrmJobStore(sp.GetRequiredService<IAsyncDbConnection>(), dialect));

        return builder;
    }
}
