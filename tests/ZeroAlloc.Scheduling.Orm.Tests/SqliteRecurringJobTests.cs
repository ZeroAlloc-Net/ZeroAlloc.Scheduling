using System.Data.Async;
using Microsoft.Data.Sqlite;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary><see cref="RecurringJobContract"/> on SQLite.</summary>
public sealed class SqliteRecurringJobTests : RecurringJobContract
{
    protected override IMigrationSource Migrations => SchedulingOrmMigrations.Sqlite;

    protected override async Task<ContractDatabase> NewDatabaseAsync(IMigrationSource? migrations = null)
    {
        var db = new Database(new SqliteFixture());
        await db.MigrateAsync(migrations ?? Migrations).ConfigureAwait(false);
        return db;
    }

    private sealed class Database(SqliteFixture fixture) : ContractDatabase
    {
        public override Task<IAsyncDbConnection> ConnectAsync() => fixture.ConnectAsync();

        protected override OrmSchedulingDialect Dialect => OrmSchedulingDialect.Sqlite;

        public override async Task<long> CountAsync(string condition)
        {
            var connection = new SqliteConnection(fixture.ConnectionString);
            await using var connectionScope = connection.ConfigureAwait(false);
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new SqliteCommand($"SELECT COUNT(*) FROM SchedulingJobs WHERE {condition}", connection);
            await using var commandScope = command.ConfigureAwait(false);
            return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }

        public override async Task<IReadOnlyList<Migration>> MigrateAsync(IMigrationSource source)
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                return await new MigrationRunner(connection, source, new SqliteMigrationDialect()).RunAsync(default).ConfigureAwait(false);
            }
        }

        protected override ValueTask DropAsync() => fixture.DisposeAsync();
    }
}
