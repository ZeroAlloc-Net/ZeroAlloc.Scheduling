using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

public sealed class InMemoryJobStoreTests
{
    private readonly InMemoryJobStore _store = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Enqueue_ThenFetchPending_ReturnsEntry()
    {
        await _store.EnqueueAsync("MyJob", [1, 2, 3], DateTimeOffset.UtcNow, 3, null, _ct);

        var entries = await _store.FetchPendingAsync(10, _ct);

        entries.Should().HaveCount(1);
        entries[0].TypeName.Should().Be("MyJob");
        entries[0].Status.Should().Be(JobStatus.Running); // atomically claimed
    }

    [Fact]
    public async Task FetchPending_SkipsFutureJobs()
    {
        await _store.EnqueueAsync("FutureJob", [], DateTimeOffset.UtcNow.AddHours(1), 3, null, _ct);

        var entries = await _store.FetchPendingAsync(10, _ct);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkSucceeded_SetsStatusToSucceeded()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.MarkSucceededAsync(entry.Id, null, null, 3, _ct);

        var all = _store.AllEntries;
        all.Should().ContainSingle(e => e.Id == entry.Id && e.Status == JobStatus.Succeeded);
    }

    [Fact]
    public async Task MarkFailed_RequeuesWithNewScheduledAt()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];
        var nextRetry = DateTimeOffset.UtcNow.AddSeconds(10);

        await _store.MarkFailedAsync(entry.Id, 1, nextRetry, _ct);

        var all = _store.AllEntries;
        var updated = all.Single(e => e.Id == entry.Id);
        updated.Status.Should().Be(JobStatus.Failed);
        updated.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task DeadLetter_SetsStatusAndError()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.DeadLetterAsync(entry.Id, "boom", _ct);

        var all = _store.AllEntries;
        var updated = all.Single(e => e.Id == entry.Id);
        updated.Status.Should().Be(JobStatus.DeadLetter);
        updated.Error.Should().Be("boom");
    }

    [Fact]
    public async Task UpsertRecurring_IdempotentByTypeName()
    {
        await _store.UpsertRecurringAsync("RecurringJob", [], DateTimeOffset.UtcNow, "0 * * * *", 1, _ct);
        await _store.UpsertRecurringAsync("RecurringJob", [], DateTimeOffset.UtcNow, "0 * * * *", 1, _ct);

        _store.AllEntries.Where(e => string.Equals(e.TypeName, "RecurringJob", StringComparison.Ordinal)).Should().HaveCount(1);
    }
}
