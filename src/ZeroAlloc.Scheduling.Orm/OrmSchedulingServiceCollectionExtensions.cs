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
    /// <see cref="IAsyncDbConnection"/> already registered in the container,
    /// using the SQLite query spelling, which PostgreSQL also accepts.
    /// </summary>
    /// <param name="builder">The scheduling builder.</param>
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
    public static ISchedulingBuilder WithOrm(this ISchedulingBuilder builder)
        => builder.WithOrm(OrmSchedulingDialect.Sqlite);

    /// <inheritdoc cref="WithOrm(ISchedulingBuilder)" />
    /// <param name="builder">The scheduling builder.</param>
    /// <param name="dialect">
    /// Which database the registered connection talks to. Only the two bounded
    /// queries differ between providers; everything else is plain ANSI.
    /// </param>
    public static ISchedulingBuilder WithOrm(
        this ISchedulingBuilder builder,
        OrmSchedulingDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<IJobStore>(sp =>
            new OrmJobStore(sp.GetRequiredService<IAsyncDbConnection>(), dialect));

        return builder;
    }

    /// <summary>
    /// Persists jobs through ZeroAlloc.ORM, against the
    /// <see cref="IAsyncDbConnection"/> registered in the container under
    /// <paramref name="connectionKey"/>.
    /// </summary>
    /// <param name="builder">The scheduling builder.</param>
    /// <param name="dialect">Which database the keyed connection talks to.</param>
    /// <param name="connectionKey">
    /// The service key of the <see cref="IAsyncDbConnection"/> to use, such as
    /// <c>services.AddKeyedScoped&lt;IAsyncDbConnection&gt;("jobs-db", ...)</c>.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Lets the job store live on a database of its own, next to an application
    /// that registers an unkeyed <see cref="IAsyncDbConnection"/> for its own
    /// data. The store resolves only the keyed connection, never the unkeyed one.
    /// Register the keyed connection yourself, as a scoped service; its lifetime,
    /// provider and connection string belong to the application.
    /// </para>
    /// <para>
    /// Like the other overloads, this registers the store with
    /// <c>TryAddScoped</c>: an <see cref="IJobStore"/> registered earlier wins.
    /// </para>
    /// <para>
    /// Create the schema with <see cref="SchedulingOrmMigrations"/> and the
    /// ORM's <c>MigrationRunner</c> on that same database.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="connectionKey"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddKeyedScoped&lt;IAsyncDbConnection&gt;("jobs-db", (sp, _) => OpenJobsDb());
    /// services.AddScheduling()
    ///         .WithOrm(OrmSchedulingDialect.Postgres, connectionKey: "jobs-db");
    /// </code>
    /// </example>
    public static ISchedulingBuilder WithOrm(
        this ISchedulingBuilder builder,
        OrmSchedulingDialect dialect,
        object connectionKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionKey);

        builder.Services.TryAddScoped<IJobStore>(sp =>
            new OrmJobStore(sp.GetRequiredKeyedService<IAsyncDbConnection>(connectionKey), dialect));

        return builder;
    }
}
