namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// Behaviour of <see cref="OrmJobStore"/> against a real SQLite database with
/// the schema applied by the ORM's MigrationRunner.
/// </summary>
public sealed class OrmJobStoreTests
{
    private static readonly byte[] s_payload = [1, 2, 3];

    private static async Task<OrmJobStore> StoreAsync(SqliteFixture fx)
        => new(await fx.ConnectAsync().ConfigureAwait(false));

    [Fact]
    public async Task Enqueue_Then_Fetch_Round_Trips()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);

        await store.EnqueueAsync("Job.A", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        var claimed = await store.FetchPendingAsync(10, default);

        claimed.Should().HaveCount(1);
        claimed[0].TypeName.Should().Be("Job.A");
        claimed[0].Payload.Should().Equal(s_payload);
        claimed[0].MaxAttempts.Should().Be(3);
        claimed[0].Status.Should().Be(JobStatus.Running, "fetching claims the job");
        claimed[0].StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Fetch_Ignores_Jobs_Scheduled_In_The_Future()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);

        await store.EnqueueAsync("Later", s_payload, DateTimeOffset.UtcNow.AddMinutes(5), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Fetch_Honours_BatchSize_And_Orders_By_ScheduledAt()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await store.EnqueueAsync($"Job.{i}", s_payload, now.AddSeconds(-10 + i), 3, null, default);
        }

        var batch = await store.FetchPendingAsync(3, default);

        batch.Should().HaveCount(3);
        batch.Should().BeInAscendingOrder(j => j.ScheduledAt);
    }

    [Fact]
    public async Task A_Claimed_Job_Is_Not_Handed_Out_Again()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        await store.EnqueueAsync("Once", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().HaveCount(1);
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty("the job is already Running");
    }

    [Fact]
    public async Task Two_Competing_Pollers_Never_Claim_The_Same_Job()
    {
        // The reason this adapter stamps a claim token rather than doing
        // select-then-update-then-reread. A read-back that filters on "candidate
        // and now Running" cannot tell our claim from someone else's, so two
        // pollers with overlapping candidates can both be handed the same job.
        // Filtering on a token only this call generated cannot confuse the two.
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var seed = await StoreAsync(fx);

        const int jobs = 40;
        var now = DateTimeOffset.UtcNow.AddSeconds(-1);
        for (var i = 0; i < jobs; i++)
        {
            await seed.EnqueueAsync($"Job.{i}", s_payload, now, 3, null, default);
        }

        var a = await StoreAsync(fx);
        var b = await StoreAsync(fx);

        // Concurrent, not sequential. Run one after the other and the second
        // poller simply finds nothing claimable, which proves very little; the
        // race only appears when both are selecting at the same time.
        var results = await Task.WhenAll(
            Task.Run(() => a.FetchPendingAsync(jobs, default).AsTask()),
            Task.Run(() => b.FetchPendingAsync(jobs, default).AsTask()));
        var claimedA = results[0];
        var claimedB = results[1];

        var idsA = claimedA.Select(j => j.Id).ToHashSet();
        var idsB = claimedB.Select(j => j.Id).ToHashSet();

        idsA.Overlaps(idsB).Should().BeFalse("a job claimed by one poller must not be claimed by the other");
        (idsA.Count + idsB.Count).Should().Be(jobs, "every job should be claimed exactly once");
    }

    [Fact]
    public async Task MarkFailed_Makes_The_Job_Claimable_Again_After_Its_Retry_Time()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        await store.EnqueueAsync("Retry", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var job = (await store.FetchPendingAsync(1, default))[0];

        await store.MarkFailedAsync(job.Id, attempts: 1, DateTimeOffset.UtcNow.AddMinutes(5), default);
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty("the retry time has not passed");

        await store.MarkFailedAsync(job.Id, attempts: 2, DateTimeOffset.UtcNow.AddSeconds(-1), default);
        var again = await store.FetchPendingAsync(10, default);

        again.Should().HaveCount(1);
        again[0].Attempts.Should().Be(2, "the attempt count must survive the round-trip");
    }

    [Fact]
    public async Task MarkSucceeded_Completes_The_Job_And_Does_Not_Requeue_A_One_Shot()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        await store.EnqueueAsync("OneShot", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var job = (await store.FetchPendingAsync(1, default))[0];

        await store.MarkSucceededAsync(job.Id, nextRunAt: null, cronExpression: null, maxAttempts: 3, default);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task MarkSucceeded_With_A_NextRun_Enqueues_The_Following_Occurrence()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        await store.EnqueueAsync("Recurring", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, "* * * * *", default);
        var job = (await store.FetchPendingAsync(1, default))[0];

        await store.MarkSucceededAsync(
            job.Id, DateTimeOffset.UtcNow.AddSeconds(-1), "* * * * *", maxAttempts: 3, default);

        var next = await store.FetchPendingAsync(10, default);

        next.Should().HaveCount(1);
        next[0].Id.Should().NotBe(job.Id, "the next occurrence is its own row");
        next[0].CronExpression.Should().Be("* * * * *");
        next[0].Payload.Should().Equal(s_payload, "the payload carries to the next occurrence");
    }

    [Fact]
    public async Task DeadLetter_Removes_It_From_The_Queue_And_Keeps_The_Error()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        await store.EnqueueAsync("Doomed", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 1, null, default);
        var job = (await store.FetchPendingAsync(1, default))[0];

        await store.DeadLetterAsync(job.Id, "boom", default);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertRecurring_Creates_Then_Updates_Rather_Than_Duplicating()
    {
        // Re-registering the same recurring job must not accumulate rows that
        // would each fire independently.
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);
        var due = DateTimeOffset.UtcNow.AddSeconds(-1);

        await store.UpsertRecurringAsync("Nightly", s_payload, due, "0 0 * * *", 3, default);
        await store.UpsertRecurringAsync("Nightly", [9, 9], due, "0 2 * * *", 5, default);

        var claimed = await store.FetchPendingAsync(10, default);

        claimed.Should().HaveCount(1, "the second registration updates the first");
        claimed[0].CronExpression.Should().Be("0 2 * * *");
        claimed[0].MaxAttempts.Should().Be(5);
        claimed[0].Payload.Should().Equal([9, 9]);
    }

    [Fact]
    public async Task Marking_A_Job_That_No_Longer_Exists_Is_A_No_Op()
    {
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();
        var store = await StoreAsync(fx);

        var act = async () => await store.MarkSucceededAsync(JobId.New(), null, null, 3, default)
            .ConfigureAwait(false);

        await act.Should().NotThrowAsync();
    }
}
