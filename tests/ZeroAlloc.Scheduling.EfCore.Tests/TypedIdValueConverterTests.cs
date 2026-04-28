using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Verifies that <see cref="JobEntryEntity.Id"/> survives a write/read round-trip
/// through the <c>TypedIdValueConverter&lt;JobId, Guid&gt;</c> registered by
/// <see cref="SchedulingDbContext"/>.
/// </summary>
public sealed class TypedIdValueConverterTests : IAsyncLifetime
{
    private SqliteConnection _conn = default!;
    private SchedulingDbContext _db = default!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var opts = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlite(_conn)
            .Options;
        _db = new SchedulingDbContext(opts);
        await _db.Database.EnsureCreatedAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync().ConfigureAwait(false);
        await _conn.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task EntityRoundTrip_PreservesJobId()
    {
        var id = JobId.New();
        _db.Jobs.Add(new JobEntryEntity
        {
            Id = id,
            TypeName = "Test",
            Payload = new byte[] { 0x01 },
            Status = JobStatus.Pending,
            MaxAttempts = 3,
            ScheduledAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();

        var roundTripped = await _db.Jobs
            .Where(e => e.Id == id)
            .FirstAsync(CancellationToken.None);

        roundTripped.Id.Should().Be(id);
    }
}
