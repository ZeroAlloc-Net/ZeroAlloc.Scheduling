using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
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
            // leave a job with two live runs. LiveRecurringFilterConvention sets its filter once
            // the column names are final.
            e.HasIndex(j => j.TypeName)
                .IsUnique()
                .HasDatabaseName(LiveRecurringIndexName);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(services =>
            new LiveRecurringFilterConvention(services.GetRequiredService<ISqlGenerationHelper>()));
    }

    /// <summary>
    /// Writes the filter of <see cref="LiveRecurringIndexName"/> from the final column names.
    /// </summary>
    /// <remarks>
    /// The filter is raw SQL, so it has to name the columns as they are in the store. A naming
    /// convention, such as the snake case one common on PostgreSQL, renames them after
    /// <see cref="OnModelCreating"/> has run, so the filter is built when the model is finalized,
    /// after every convention the consumer added. The names are quoted the way the provider quotes
    /// them, because PostgreSQL folds unquoted names to lower case and would not find quoted columns.
    /// The status values are those of <see cref="JobStatus"/>, which the Status column stores as an
    /// integer.
    /// </remarks>
    private sealed class LiveRecurringFilterConvention(ISqlGenerationHelper sql) : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            var entity = modelBuilder.Metadata.FindEntityType(typeof(JobEntryEntity))
                ?? throw new InvalidOperationException($"The model has no {nameof(JobEntryEntity)}.");
            var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)
                ?? throw new InvalidOperationException($"{nameof(JobEntryEntity)} is not mapped to a table.");
            var index = entity.FindIndex(entity.FindProperty(nameof(JobEntryEntity.TypeName))!)
                ?? throw new InvalidOperationException($"The model has no index {LiveRecurringIndexName}.");

            var cron = sql.DelimitIdentifier(ColumnName(entity, nameof(JobEntryEntity.CronExpression), table));
            var status = sql.DelimitIdentifier(ColumnName(entity, nameof(JobEntryEntity.Status), table));
            var filter = string.Create(CultureInfo.InvariantCulture,
                $"{cron} IS NOT NULL AND {status} IN ({(int)JobStatus.Pending}, {(int)JobStatus.Running}, {(int)JobStatus.Failed})");

            if (index.Builder.HasFilter(filter, fromDataAnnotation: true) is null)
                throw new InvalidOperationException($"The filter of {LiveRecurringIndexName} could not be set.");
        }

        private static string ColumnName(IConventionEntityType entity, string property, StoreObjectIdentifier table) =>
            entity.FindProperty(property)?.GetColumnName(table)
            ?? throw new InvalidOperationException($"{nameof(JobEntryEntity)}.{property} has no column in {table.DisplayName()}.");
    }
}
