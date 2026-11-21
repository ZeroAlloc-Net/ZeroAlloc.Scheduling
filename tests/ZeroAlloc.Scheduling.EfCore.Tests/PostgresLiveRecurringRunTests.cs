using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// <see cref="LiveRecurringRunContract"/> on a real PostgreSQL, which folds unquoted names in the
/// index filter to lower case. One container for the class, one database per test.
/// </summary>
public sealed class PostgresLiveRecurringRunTests : LiveRecurringRunContract, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    protected override async Task<DbContextOptions<SchedulingDbContext>> NewDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "jobs_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var db = new SchedulingDbContext(options);
        await using (db.ConfigureAwait(false))
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return options;
    }
}
