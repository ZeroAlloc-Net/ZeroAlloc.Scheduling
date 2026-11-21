using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Checks that a context whose columns a naming convention renamed builds the live-run index filter
/// from the renamed columns.
/// </summary>
internal static class SnakeCaseColumns
{
    public static void AssertFilterUsesRenamedColumns(DbContextOptions<SchedulingDbContext> options)
    {
        using var db = new SchedulingDbContext(options);
        var entity = db.Model.FindEntityType(typeof(JobEntryEntity))!;
        var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
        entity.FindProperty(nameof(JobEntryEntity.CronExpression))!.GetColumnName(table)
            .Should().Be("cron_expression", "the naming convention must have renamed the columns");

        var filter = entity.GetIndexes().Single(i => string.Equals(i.GetDatabaseName(), "UX_ScheduledJobs_LiveRecurring", StringComparison.Ordinal)).GetFilter();

        filter.Should().Contain("cron_expression").And.Contain("status")
            .And.NotContain("CronExpression").And.NotContain("Status");
    }
}
