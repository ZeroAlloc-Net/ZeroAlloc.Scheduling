using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// <see cref="LiveRecurringRunContract"/> on a real PostgreSQL with the snake case naming convention
/// that many PostgreSQL applications use, which renames the columns the index filter names. One
/// container for the class, one database per test.
/// </summary>
public sealed class PostgresSnakeCaseLiveRecurringRunTests : LiveRecurringRunContract, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public void The_Index_Filter_Names_The_Renamed_Columns() =>
        SnakeCaseColumns.AssertFilterUsesRenamedColumns(Options(_container.GetConnectionString()));

    protected override async Task<DbContextOptions<SchedulingDbContext>> NewDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "jobs_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
        var options = Options(connectionString);
        var db = new SchedulingDbContext(options);
        await using (db.ConfigureAwait(false))
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return options;
    }

    private static DbContextOptions<SchedulingDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
}
