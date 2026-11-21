using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// <see cref="LiveRecurringRunContract"/> on a real SQL Server, whose filtered unique index and
/// locking differ from the other two. One container for the class, one database per test.
/// </summary>
public sealed class SqlServerLiveRecurringRunTests : LiveRecurringRunContract, IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    protected override async Task<DbContextOptions<SchedulingDbContext>> NewDatabaseAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "jobs_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var db = new SchedulingDbContext(options);
        await using (db.ConfigureAwait(false))
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return options;
    }
}
