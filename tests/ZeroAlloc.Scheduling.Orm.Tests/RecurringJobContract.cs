using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// One live run per recurring job type, whatever the number of instances that start or poll,
/// checked on every database the store supports. A live run is one that is Pending, Running or
/// Failed and waiting for its retry; completed runs are kept as history.
/// </summary>
public abstract class RecurringJobContract
{
    private const string Hourly = "0 * * * *";
    private const string Yearly = "0 0 1 1 *";
    private static readonly TimeSpan ShortClaimTimeout = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan ExpiredClaimDelay = TimeSpan.FromMilliseconds(50);
    private static readonly byte[] s_payload = [4, 2];

    /// <summary>A new, empty database, migrated unless <paramref name="migrations"/> says otherwise.</summary>
    protected abstract Task<ContractDatabase> NewDatabaseAsync(IMigrationSource? migrations = null);

    /// <summary>The migration source for this database.</summary>
    protected abstract IMigrationSource Migrations { get; }

    [Fact]
    public async Task A_Restart_After_A_Run_Keeps_One_Live_Run()
    {
        await using var db = await NewDatabaseAsync();
        var due = DateTimeOffset.UtcNow.AddSeconds(-1);

        await (await db.StoreAsync()).UpsertRecurringAsync("Hourly", s_payload, due, Hourly, 3, default);
        var worker = await db.StoreAsync();
        var run = (await worker.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;
        await worker.MarkSucceededAsync(run.Id, DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);

        // The next instance to start seeds the same job again.
        await (await db.StoreAsync()).UpsertRecurringAsync("Hourly", s_payload, DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);

        (await db.CountAsync("Status IN (0, 1, 3)")).Should().Be(1, "the restart must not revive the completed run");
        (await db.CountAsync("Status = 2")).Should().Be(1);
    }

    [Fact]
    public async Task Instances_Starting_At_Once_Seed_One_Run()
    {
        await using var db = await NewDatabaseAsync();
        var stores = new List<OrmJobStore>();
        for (var i = 0; i < 8; i++)
        {
            stores.Add(await db.StoreAsync());
        }

        await Task.WhenAll(stores.Select(store => Task.Run(() =>
            store.UpsertRecurringAsync("Hourly", s_payload, DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default).AsTask())));

        (await db.CountAsync("1 = 1")).Should().Be(1);
    }

    [Fact]
    public async Task Seeding_While_A_Run_Is_Running_Or_Retrying_Leaves_It_Alone()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync();
        await store.UpsertRecurringAsync("Hourly", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), Hourly, 3, default);
        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        await store.UpsertRecurringAsync("Hourly", [9], DateTimeOffset.UtcNow.AddHours(1), Hourly, 5, default);
        (await db.CountAsync("1 = 1")).Should().Be(1, "the running run is the live one");
        (await db.CountAsync("Status = 1 AND MaxAttempts = 3")).Should().Be(1, "the running run keeps its definition");

        await store.MarkFailedAsync(run.Id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), default);
        await store.UpsertRecurringAsync("Hourly", [9], DateTimeOffset.UtcNow.AddHours(1), Hourly, 5, default);
        (await db.CountAsync("1 = 1")).Should().Be(1, "the run waiting for its retry is the live one");

        // Still due for its retry, with its attempt count and its own payload.
        var retry = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;
        retry.Id.Should().Be(run.Id);
        retry.Attempts.Should().Be(1);
        retry.MaxAttempts.Should().Be(3);
        retry.Payload.Should().Equal(s_payload);
    }

    [Fact]
    public async Task Seeding_With_The_Same_Schedule_Keeps_The_Due_Time()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync();
        await store.UpsertRecurringAsync("Report", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), Hourly, 3, default);

        await store.UpsertRecurringAsync("Report", [7], DateTimeOffset.UtcNow.AddHours(1), Hourly, 5, default);

        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle("the run stays due").Subject;
        run.MaxAttempts.Should().Be(5);
        run.Payload.Should().Equal([7]);
    }

    [Fact]
    public async Task Seeding_With_A_New_Schedule_Moves_The_Pending_Run()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync();
        await store.UpsertRecurringAsync("Report", s_payload, DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);

        await store.UpsertRecurringAsync("Report", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), "* * * * *", 3, default);

        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle("the new schedule makes it due").Subject;
        run.CronExpression.Should().Be("* * * * *");
    }

    [Fact]
    public async Task Competing_Pollers_Run_A_Due_Job_Once()
    {
        await using var db = await NewDatabaseAsync();
        var pollers = new List<OrmJobStore>();
        for (var i = 0; i < 4; i++)
        {
            pollers.Add(await db.StoreAsync());
        }

        var store = await db.StoreAsync();
        var claims = new Task<IReadOnlyList<JobEntry>>[pollers.Count];
        for (var round = 0; round < 25; round++)
        {
            await store.EnqueueAsync($"Race.{round}", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

            for (var i = 0; i < pollers.Count; i++)
            {
                var poller = pollers[i];
                claims[i] = Task.Run(() => poller.FetchPendingAsync(10, default).AsTask());
            }

            var claimed = 0;
            foreach (var claim in await Task.WhenAll(claims))
            {
                claimed += claim.Count;
            }

            claimed.Should().Be(1, $"round {round} has one due job");
        }
    }

    [Fact]
    public async Task The_Upgrade_Keeps_The_Earliest_Of_Duplicate_Live_Runs()
    {
        // Before version 3 the store could leave several live runs of one recurring job.
        var shipped = new FirstMigrations(Migrations, 2);
        await using var db = await NewDatabaseAsync(shipped);
        var store = await db.StoreAsync();
        var first = DateTimeOffset.UtcNow.AddMinutes(10);
        // MaxAttempts tells the duplicates apart; 4 is the earliest.
        await store.EnqueueAsync("Hourly", s_payload, first.AddMinutes(30), 3, Hourly, default);
        await store.EnqueueAsync("Hourly", s_payload, first, 4, Hourly, default);
        await store.EnqueueAsync("Hourly", s_payload, first.AddMinutes(50), 5, Hourly, default);
        await store.EnqueueAsync("OneOff", s_payload, first, 3, null, default);
        await store.EnqueueAsync("OneOff", s_payload, first, 3, null, default);

        var applied = await db.MigrateAsync(Migrations);

        applied.Select(m => m.Version).Should().Equal(3);
        (await db.CountAsync("TypeName = 'Hourly'")).Should().Be(1);
        (await db.CountAsync("TypeName = 'Hourly' AND MaxAttempts = 4")).Should().Be(1, "the earliest run is kept");
        (await db.CountAsync("TypeName = 'OneOff'")).Should().Be(2, "one-off jobs are not recurring runs");
        await store.UpsertRecurringAsync("Hourly", s_payload, first.AddHours(2), Hourly, 3, default);
        (await db.CountAsync("TypeName = 'Hourly'")).Should().Be(1);
    }

    [Fact]
    public async Task A_Running_Claim_Older_Than_The_Claim_Timeout_Is_Claimed_Again()
    {
        await using var db = await NewDatabaseAsync();
        var abandoned = await db.StoreAsync();
        await abandoned.EnqueueAsync("Abandoned", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var run = (await abandoned.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        (await (await db.StoreAsync(TimeSpan.FromHours(1))).FetchPendingAsync(10, default))
            .Should().BeEmpty("the claim is newer than the timeout");

        await Task.Delay(ExpiredClaimDelay);
        var retaken = (await (await db.StoreAsync(ShortClaimTimeout)).FetchPendingAsync(10, default))
            .Should().ContainSingle("the claim is older than the timeout").Subject;

        retaken.Id.Should().Be(run.Id);
        retaken.Status.Should().Be(JobStatus.Running);
        retaken.Attempts.Should().Be(run.Attempts + 1, "the abandoned run counts as an attempt");
    }

    [Fact]
    public async Task The_Longest_Claim_Timeout_Never_Expires_A_Claim()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync(TimeSpan.MaxValue);
        await store.EnqueueAsync("Job", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().ContainSingle();
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(Outcome.Succeeded)]
    [InlineData(Outcome.Failed)]
    [InlineData(Outcome.DeadLetter)]
    public async Task A_Late_Outcome_Of_An_Abandoned_Claim_Does_Not_Overwrite_The_New_Run(Outcome outcome)
    {
        await using var db = await NewDatabaseAsync();
        var abandoned = await db.StoreAsync();
        await abandoned.UpsertRecurringAsync("Hourly", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), Hourly, 3, default);
        var run = (await abandoned.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        await Task.Delay(ExpiredClaimDelay);
        var reclaimer = await db.StoreAsync(ShortClaimTimeout);
        var retaken = (await reclaimer.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        var nextRun = DateTimeOffset.UtcNow.AddHours(1);
        switch (outcome)
        {
            case Outcome.Succeeded:
                await abandoned.MarkSucceededAsync(run.Id, nextRun, Hourly, 3, default);
                break;
            case Outcome.Failed:
                await abandoned.MarkFailedAsync(run.Id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), default);
                break;
            default:
                await abandoned.DeadLetterAsync(run.Id, "late", default);
                break;
        }

        (await db.CountAsync("1 = 1")).Should().Be(1, "the late outcome adds no next occurrence");
        (await db.CountAsync($"Status = 1 AND Attempts = {retaken.Attempts} AND Error IS NULL"))
            .Should().Be(1, "the run that took the claim back is still running");

        await reclaimer.MarkSucceededAsync(retaken.Id, nextRun, Hourly, 3, default);
        (await db.CountAsync("Status = 2")).Should().Be(1);
        (await db.CountAsync("Status = 0")).Should().Be(1);
    }

    [Fact]
    public async Task A_Recurring_Job_That_Always_Fails_Is_Dead_Lettered_And_Keeps_Its_Schedule()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync();
        await store.UpsertRecurringAsync("Failing", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), Yearly, 2, default);
        var executor = new AlwaysFailingExecutor("Failing");
        const string Successor = "Status = 0 AND CronExpression IS NOT NULL AND Attempts = 0";

        await using (var services = new ServiceCollection()
            .AddSingleton<IJobStore>(store)
            .AddSingleton<IJobTypeExecutor>(executor)
            .AddScheduling(o =>
            {
                o.PollingInterval = TimeSpan.FromMilliseconds(20);
                o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            })
            .Services.BuildServiceProvider())
        {
            var worker = services.GetRequiredService<SchedulingWorkerService>();
            await worker.StartAsync(default);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while ((await db.CountAsync("Status = 4") == 0 || await db.CountAsync(Successor) == 0)
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            await worker.StopAsync(default);
        }

        // The worker inserts the next occurrence right after the dead-letter; stopping it in
        // between would cancel that insert, so the wait above is for both.
        executor.Calls.Should().Be(2, "retries of the occurrence stay bounded by its maximum attempts");
        (await db.CountAsync("Status = 4 AND Error IS NOT NULL")).Should().Be(1, "the dead-lettered occurrence keeps its error");
        (await db.CountAsync(Successor)).Should().Be(1, "the next occurrence is scheduled");
        (await (await db.StoreAsync()).FetchPendingAsync(10, default))
            .Should().BeEmpty("the next occurrence is not due before next year");
    }

    [Fact]
    public async Task The_Periodic_Seed_Brings_Back_A_Lost_Run_And_The_Current_Definition()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync();
        const string Sweep = "TypeName = 'ZeroAlloc.Scheduling.Orm.Tests.YearlySweep'";
        const string Live = Sweep + " AND Status IN (0, 1, 3)";

        await using var services = new ServiceCollection()
            .AddSingleton<IJobStore>(store)
            .AddScheduling(o => o.ClaimTimeout = TimeSpan.FromMilliseconds(100))
            .AddYearlySweepJob()
            .Services.BuildServiceProvider();
        var seed = services.GetServices<IHostedService>().OfType<YearlySweepRecurringStartup>().Single();

        await seed.StartAsync(default);
        try
        {
            (await db.CountAsync(Live)).Should().Be(1, "the seed at startup schedules the job");

            // A dead-letter or a success whose next-occurrence insert failed leaves no live run.
            await db.ExecuteAsync($"UPDATE SchedulingJobs SET Status = 4 WHERE {Sweep}");
            await WaitForCountAsync(db, Live + " AND MaxAttempts = 3", 1);

            // The upsert of a stale run put an old definition on the pending run.
            await db.ExecuteAsync($"UPDATE SchedulingJobs SET MaxAttempts = 9 WHERE {Sweep} AND Status = 0");
            await WaitForCountAsync(db, Live + " AND MaxAttempts = 3", 1);
        }
        finally
        {
            await seed.StopAsync(default);
        }

        (await db.CountAsync(Live)).Should().Be(1, "repeating the seed adds no second live run");
        (await db.CountAsync(Sweep + " AND Status = 4")).Should().Be(1);
    }

    private static async Task WaitForCountAsync(ContractDatabase db, string condition, long expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await db.CountAsync(condition).ConfigureAwait(false) != expected)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"No {expected} rows matched '{condition}' within the deadline.");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    /// <summary>The outcome a run records.</summary>
    public enum Outcome
    {
        Succeeded,
        Failed,
        DeadLetter,
    }

    /// <summary>The first <c>count</c> migrations of a source, under its name, as an older release shipped them.</summary>
    private sealed class FirstMigrations(IMigrationSource source, int count) : IMigrationSource
    {
        public string Name => source.Name;

        public IReadOnlyList<Migration> GetMigrations() => source.GetMigrations().Take(count).ToList();
    }
}
