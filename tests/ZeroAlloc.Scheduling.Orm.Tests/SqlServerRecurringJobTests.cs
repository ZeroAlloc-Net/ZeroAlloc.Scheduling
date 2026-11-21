using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// <see cref="RecurringJobContract"/> on a real SQL Server, whose filtered unique index and locking
/// differ from the other two. One container for the class, one database per test.
/// </summary>
public sealed class SqlServerRecurringJobTests : RecurringJobContract, IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    protected override IMigrationSource Migrations => SchedulingOrmMigrations.SqlServer;

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    protected override async Task<ContractDatabase> NewDatabaseAsync(IMigrationSource? migrations = null)
    {
        var name = "jobs_" + Guid.NewGuid().ToString("N");
        var admin = new SqlConnection(_container.GetConnectionString());
        await using (admin.ConfigureAwait(false))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            var create = new SqlCommand($"CREATE DATABASE {name}", admin);
            await using var createScope = create.ConfigureAwait(false);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var db = new Database(new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString);
        await db.MigrateAsync(migrations ?? Migrations).ConfigureAwait(false);
        return db;
    }

    private sealed class Database(string connectionString) : ContractDatabase
    {
        public override async Task<IAsyncDbConnection> ConnectAsync()
        {
            var connection = new SqlConnection(connectionString).AsAsync();
            await connection.OpenAsync().ConfigureAwait(false);
            return connection;
        }

        protected override OrmSchedulingDialect Dialect => OrmSchedulingDialect.SqlServer;

        public override async Task<long> CountAsync(string condition)
        {
            var connection = new SqlConnection(connectionString);
            await using var connectionScope = connection.ConfigureAwait(false);
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM SchedulingJobs WHERE {condition}", connection);
            await using var commandScope = command.ConfigureAwait(false);
            return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        public override async Task ExecuteAsync(string sql)
        {
            var connection = new SqlConnection(connectionString);
            await using var connectionScope = connection.ConfigureAwait(false);
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new SqlCommand(sql, connection);
            await using var commandScope = command.ConfigureAwait(false);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public override async Task<IReadOnlyList<Migration>> MigrateAsync(IMigrationSource source)
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return await new MigrationRunner(connection, source, new SqlServerMigrationDialect()).RunAsync(default).ConfigureAwait(false);
            }
        }

        protected override ValueTask DropAsync()
        {
            SqlConnection.ClearAllPools();
            return ValueTask.CompletedTask;
        }
    }
}
