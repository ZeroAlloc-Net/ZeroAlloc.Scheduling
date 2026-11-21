using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ZeroAlloc.ValueObjects.EfCore;

namespace ZeroAlloc.Scheduling.EfCore;

public sealed class SchedulingDbContext : DbContext
{
    /// <summary>
    /// The name of the unique index that allows one live run per recurring job.
    /// </summary>
    private const string LiveRecurringIndexName = "UX_ScheduledJobs_LiveRecurring";

    [RequiresUnreferencedCode("EF Core builds its model with reflection and is not trim-safe; see https://aka.ms/efcore-docs-trimming. Under trimming or NativeAOT use an AOT-compatible job store instead: ZeroAlloc.Scheduling.Orm, ZeroAlloc.Scheduling.Redis or ZeroAlloc.Scheduling.InMemory.")]
    [RequiresDynamicCode("EF Core generates code at runtime and is not NativeAOT-compatible; see https://aka.ms/efcore-docs-trimming. Under NativeAOT use an AOT-compatible job store instead: ZeroAlloc.Scheduling.Orm, ZeroAlloc.Scheduling.Redis or ZeroAlloc.Scheduling.InMemory.")]
    public SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : base(options) { }

    public DbSet<JobEntryEntity> Jobs => Set<JobEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Store DateTimeOffset as UTC ticks (long) so SQLite can compare them correctly.
        var dtoConverter = new ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        var dtoNullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            v => v == null ? null : v.Value.UtcTicks,
            v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));

        modelBuilder.Entity<JobEntryEntity>(e =>
        {
            e.ToTable("ScheduledJobs");
            e.HasKey(j => j.Id);
            e.Property(j => j.Id).HasConversion<TypedIdValueConverter<JobId, Guid>>();
            e.Property(j => j.TypeName).HasMaxLength(256).IsRequired();
            e.Property(j => j.Status).HasConversion<int>();
            e.Property(j => j.Error).HasMaxLength(2000);
            e.Property(j => j.CronExpression).HasMaxLength(256);
            e.Property(j => j.ScheduledAt).HasConversion(dtoConverter);
            e.Property(j => j.StartedAt).HasConversion(dtoNullableConverter);
            e.Property(j => j.CompletedAt).HasConversion(dtoNullableConverter);
            e.Property(j => j.NextRunAt).HasConversion(dtoNullableConverter);
            e.HasIndex(j => new { j.Status, j.ScheduledAt });
            // The claim read-back filters on ClaimToken alone, so without this
            // every poll scans a table that grows without bound.
            e.HasIndex(j => j.ClaimToken);
            // One live run, Pending, Running or Failed, per recurring job type. Finished runs are
            // history and fall outside the filter. The index turns the recurring seed into an
            // insert that the database rejects when a live run already exists, so instances that
            // seed at once, or a seed racing the next occurrence after a dead-letter, cannot
            // leave a job with two live runs.
            e.HasIndex(j => j.TypeName)
                .IsUnique()
                .HasDatabaseName(LiveRecurringIndexName)
                .HasFilter(LiveRecurringFilter(this.GetService<ISqlGenerationHelper>()));
        });
    }

    // The filter is raw SQL, so the column names are quoted the way the provider quotes them:
    // PostgreSQL folds unquoted names to lower case and would not find the columns otherwise.
    // The status values are those of JobStatus, which the Status column stores as an integer.
    private static string LiveRecurringFilter(ISqlGenerationHelper sql)
    {
        var cron = sql.DelimitIdentifier(nameof(JobEntryEntity.CronExpression));
        var status = sql.DelimitIdentifier(nameof(JobEntryEntity.Status));
        return string.Create(CultureInfo.InvariantCulture, $"{cron} IS NOT NULL AND {status} IN ({(int)JobStatus.Pending}, {(int)JobStatus.Running}, {(int)JobStatus.Failed})");
    }
}
