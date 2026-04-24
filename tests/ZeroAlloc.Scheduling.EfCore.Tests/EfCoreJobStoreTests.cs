using Microsoft.EntityFrameworkCore;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

public sealed class EfCoreJobStoreTests : IDisposable
{
    private readonly SchedulingDbContext _db;
    private readonly EfCoreJobStore _store;
    private readonly CancellationToken _ct = CancellationToken.None;

    public EfCoreJobStoreTests()
    {
        var opts = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlite($"Data Source={Path.GetTempFileName()}")
            .Options;
        _db = new SchedulingDbContext(opts);
        _db.Database.EnsureCreated();
        _store = new EfCoreJobStore(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Enqueue_ThenFetchPending_ReturnsRunningEntry()
    {
        await _store.EnqueueAsync("MyJob", [1, 2, 3], DateTimeOffset.UtcNow, 3, null, _ct);
        var entries = await _store.FetchPendingAsync(10, _ct);

        entries.Should().HaveCount(1);
        entries[0].TypeName.Should().Be("MyJob");
        entries[0].Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task FetchPending_SkipsFutureJobs()
    {
        await _store.EnqueueAsync("FutureJob", [], DateTimeOffset.UtcNow.AddHours(1), 3, null, _ct);
        var entries = await _store.FetchPendingAsync(10, _ct);
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkSucceeded_UpdatesStatus()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.MarkSucceededAsync(entry.Id, null, null, 3, _ct);

        var updated = await _db.Jobs.FindAsync(entry.Id.Value);
        updated!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task DeadLetter_SetsError()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.DeadLetterAsync(entry.Id, "oops", _ct);

        var updated = await _db.Jobs.FindAsync(entry.Id.Value);
        updated!.Status.Should().Be(JobStatus.DeadLetter);
        updated.Error.Should().Be("oops");
    }

    [Fact]
    public async Task UpsertRecurring_IdempotentByTypeName()
    {
        await _store.UpsertRecurringAsync("Recurring", [], DateTimeOffset.UtcNow, "0 * * * *", 1, _ct);
        await _store.UpsertRecurringAsync("Recurring", [], DateTimeOffset.UtcNow, "0 * * * *", 1, _ct);

        var count = await _db.Jobs.CountAsync(j => j.TypeName == "Recurring");
        count.Should().Be(1);
    }
}
