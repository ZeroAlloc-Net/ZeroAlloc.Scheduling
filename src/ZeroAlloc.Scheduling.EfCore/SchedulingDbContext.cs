using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ZeroAlloc.Scheduling.EfCore;

public sealed class SchedulingDbContext : DbContext
{
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
            e.Property(j => j.TypeName).HasMaxLength(256).IsRequired();
            e.Property(j => j.Status).HasConversion<int>();
            e.Property(j => j.Error).HasMaxLength(2000);
            e.Property(j => j.CronExpression).HasMaxLength(256);
            e.Property(j => j.ScheduledAt).HasConversion(dtoConverter);
            e.Property(j => j.StartedAt).HasConversion(dtoNullableConverter);
            e.Property(j => j.CompletedAt).HasConversion(dtoNullableConverter);
            e.Property(j => j.NextRunAt).HasConversion(dtoNullableConverter);
            e.HasIndex(j => new { j.Status, j.ScheduledAt });
        });
    }
}
