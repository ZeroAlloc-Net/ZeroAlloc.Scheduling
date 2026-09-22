using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// A SQLite database living as long as the fixture, with the job schema applied
/// through the ORM's own <see cref="MigrationRunner"/>.
/// </summary>
/// <remarks>
/// Shared-cache in-memory rather than plain <c>:memory:</c>, because the claim
/// tests need two connections competing over the same rows. The keep-alive
/// connection holds the database open, since a shared-cache database is dropped
/// when its last connection closes.
/// </remarks>
public sealed class SqliteFixture : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;

    public SqliteFixture()
    {
        ConnectionString = $"Data Source=jobs-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    public async Task<IAsyncDbConnection> ConnectAsync(CancellationToken ct = default)
    {
        var raw = new SqliteConnection(ConnectionString);
        await raw.OpenAsync(ct).ConfigureAwait(false);
        return raw.AsAsync();
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var connection = await ConnectAsync(ct).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var runner = new MigrationRunner(
                connection, SchedulingOrmMigrations.Sqlite, new SqliteMigrationDialect());
            await runner.RunAsync(ct).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _keepAlive.Dispose();
        return ValueTask.CompletedTask;
    }
}
