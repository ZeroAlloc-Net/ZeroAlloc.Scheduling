using ZeroAlloc.TestHelpers;
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

    [Fact]
    public async Task RequeueAsync_MovesDeadLetterToPending()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];
        await _store.DeadLetterAsync(entry.Id, "error", _ct);

        await _store.RequeueAsync(entry.Id, _ct);

        var updated = _store.AllEntries.Single(e => e.Id == entry.Id);
        updated.Status.Should().Be(JobStatus.Pending);
        updated.Error.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.DeleteAsync(entry.Id, _ct);

        _store.AllEntries.Should().BeEmpty();
    }

    // Scheduling#376: finished jobs stay queryable for the dashboard, but the poll must
    // neither scan them nor allocate per scanned entry.

    [Fact]
    public async Task FetchPending_DoesNotAllocate_PerFinishedEntry()
    {
        await FinishJobsAsync(succeeded: 500, deadLettered: 500);
        // One pending job that is not yet due, so the poll has something to look at.
        await _store.EnqueueAsync("FutureJob", [], DateTimeOffset.UtcNow.AddHours(1), 3, null, _ct);

        // A poll that claims nothing must not allocate, however many jobs finished.
        AllocationGate.AssertBudgetValueTask(
            0, 100, () => _store.FetchPendingAsync(10, _ct), "InMemoryJobStore.FetchPendingAsync");
    }

    [Fact]
    public async Task FinishedJobs_StayQueryable_ForTheDashboard()
    {
        await FinishJobsAsync(succeeded: 3, deadLettered: 2);

        var summary = await _store.GetSummaryAsync(_ct);
        summary.Succeeded.Should().Be(3);
        summary.DeadLetter.Should().Be(2);
        (await _store.QueryByStatusAsync([JobStatus.Succeeded, JobStatus.DeadLetter], ct: _ct))
            .Should().HaveCount(5);
        (await _store.FetchPendingAsync(10, _ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task MarkFailed_JobIsClaimableAgain_OnceDue()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.MarkFailedAsync(entry.Id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), _ct);

        var retried = await _store.FetchPendingAsync(10, _ct);
        retried.Should().ContainSingle(e => e.Id == entry.Id && e.Status == JobStatus.Running);
        (await _store.FetchPendingAsync(10, _ct)).Should().BeEmpty("a claimed job is claimed once");
    }

    [Fact]
    public async Task RequeueAsync_SucceededJob_IsClaimableAgain()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];
        await _store.MarkSucceededAsync(entry.Id, null, null, 3, _ct);

        await _store.RequeueAsync(entry.Id, _ct);

        (await _store.FetchPendingAsync(10, _ct)).Should().ContainSingle(e => e.Id == entry.Id);
    }

    [Fact]
    public async Task MarkSucceeded_Recurring_NextRunIsClaimable()
    {
        await _store.EnqueueAsync("Cron", [], DateTimeOffset.UtcNow, 3, "* * * * *", _ct);
        var entry = (await _store.FetchPendingAsync(1, _ct))[0];

        await _store.MarkSucceededAsync(entry.Id, DateTimeOffset.UtcNow.AddSeconds(-1), "* * * * *", 3, _ct);

        var next = await _store.FetchPendingAsync(10, _ct);
        next.Should().ContainSingle(e => e.Id != entry.Id && e.TypeName == "Cron");
    }

    [Fact]
    public async Task DeleteAsync_PendingJob_IsNotClaimed()
    {
        await _store.EnqueueAsync("MyJob", [], DateTimeOffset.UtcNow, 3, null, _ct);
        var id = _store.AllEntries.Single().Id;

        await _store.DeleteAsync(id, _ct);

        (await _store.FetchPendingAsync(10, _ct)).Should().BeEmpty();
    }

    private async Task FinishJobsAsync(int succeeded, int deadLettered)
    {
        for (var i = 0; i < succeeded + deadLettered; i++)
            await _store.EnqueueAsync("Done", [], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, _ct).ConfigureAwait(false);

        var claimed = await _store.FetchPendingAsync(succeeded + deadLettered, _ct).ConfigureAwait(false);
        claimed.Should().HaveCount(succeeded + deadLettered);
        for (var i = 0; i < claimed.Count; i++)
        {
            if (i < succeeded)
                await _store.MarkSucceededAsync(claimed[i].Id, null, null, 3, _ct).ConfigureAwait(false);
            else
                await _store.DeadLetterAsync(claimed[i].Id, "boom", _ct).ConfigureAwait(false);
        }
    }
}
