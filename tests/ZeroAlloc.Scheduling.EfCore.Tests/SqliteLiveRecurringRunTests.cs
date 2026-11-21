using Microsoft.EntityFrameworkCore;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary><see cref="LiveRecurringRunContract"/> on SQLite, one database file per test.</summary>
public sealed class SqliteLiveRecurringRunTests : LiveRecurringRunContract
{
    protected override async Task<DbContextOptions<SchedulingDbContext>> NewDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlite($"Data Source={Path.GetTempFileName()}")
            .Options;
        var db = new SchedulingDbContext(options);
        await using (db.ConfigureAwait(false))
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return options;
    }
}
