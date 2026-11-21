using Microsoft.EntityFrameworkCore;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// <see cref="LiveRecurringRunContract"/> on SQLite with a snake case naming convention, which
/// renames the columns the index filter names.
/// </summary>
public sealed class SqliteSnakeCaseLiveRecurringRunTests : LiveRecurringRunContract
{
    [Fact]
    public void The_Index_Filter_Names_The_Renamed_Columns() =>
        SnakeCaseColumns.AssertFilterUsesRenamedColumns(Options($"Data Source={Path.GetTempFileName()}"));

    protected override async Task<DbContextOptions<SchedulingDbContext>> NewDatabaseAsync()
    {
        var options = Options($"Data Source={Path.GetTempFileName()}");
        var db = new SchedulingDbContext(options);
        await using (db.ConfigureAwait(false))
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return options;
    }

    private static DbContextOptions<SchedulingDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
}
