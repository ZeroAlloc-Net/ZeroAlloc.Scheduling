using System.Data.Async;
using System.Data.Async.Adapters;
using Npgsql;
using Testcontainers.PostgreSql;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// <see cref="RecurringJobContract"/> on a real PostgreSQL, the only way to prove the claim and the
/// recurring seed under PostgreSQL's own row locking. One container for the class, one database per test.
/// </summary>
public sealed class PostgresRecurringJobTests : RecurringJobContract, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    protected override IMigrationSource Migrations => SchedulingOrmMigrations.Postgres;

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    protected override async Task<ContractDatabase> NewDatabaseAsync(IMigrationSource? migrations = null)
    {
        var name = "jobs_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnection(_container.GetConnectionString());
        await using (admin.ConfigureAwait(false))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await using var createScope = create.ConfigureAwait(false);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var db = new Database(
            NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString));
        await db.MigrateAsync(migrations ?? Migrations).ConfigureAwait(false);
        return db;
    }

    private sealed class Database(NpgsqlDataSource dataSource) : ContractDatabase
    {
        public override async Task<IAsyncDbConnection> ConnectAsync()
        {
            var connection = dataSource.CreateConnection().AsAsync();
            await connection.OpenAsync().ConfigureAwait(false);
            return connection;
        }

        protected override OrmSchedulingDialect Dialect => OrmSchedulingDialect.Postgres;

        public override async Task<long> CountAsync(string condition)
        {
            var command = dataSource.CreateCommand($"SELECT COUNT(*) FROM SchedulingJobs WHERE {condition}");
            await using var commandScope = command.ConfigureAwait(false);
            return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        public override async Task<IReadOnlyList<Migration>> MigrateAsync(IMigrationSource source)
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return await new MigrationRunner(connection, source, new PostgresMigrationDialect()).RunAsync(default).ConfigureAwait(false);
            }
        }

        protected override ValueTask DropAsync() => dataSource.DisposeAsync();
    }
}
