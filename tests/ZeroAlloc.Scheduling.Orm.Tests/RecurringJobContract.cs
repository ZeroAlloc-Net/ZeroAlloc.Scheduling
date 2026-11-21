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
    public async Task Seeding_While_A_Run_Is_Running_Or_Retrying_Adds_Nothing()
    {
        await using var db = await NewDatabaseAsync();
        var store = await db.StoreAsync();
        await store.UpsertRecurringAsync("Hourly", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), Hourly, 3, default);
        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        await store.UpsertRecurringAsync("Hourly", [9], DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);
        (await db.CountAsync("1 = 1")).Should().Be(1, "the running run is the live one");

        await store.MarkFailedAsync(run.Id, 1, DateTimeOffset.UtcNow.AddMinutes(1), default);
        await store.UpsertRecurringAsync("Hourly", [9], DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);
        (await db.CountAsync("1 = 1")).Should().Be(1, "the run waiting for its retry is the live one");
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
        await store.EnqueueAsync("Hourly", s_payload, first.AddMinutes(30), 3, Hourly, default);
        await store.EnqueueAsync("Hourly", s_payload, first, 3, Hourly, default);
        await store.EnqueueAsync("Hourly", s_payload, first.AddMinutes(50), 3, Hourly, default);
        await store.EnqueueAsync("OneOff", s_payload, first, 3, null, default);
        await store.EnqueueAsync("OneOff", s_payload, first, 3, null, default);

        var applied = await db.MigrateAsync(Migrations);

        applied.Select(m => m.Version).Should().Equal(3);
        (await db.CountAsync("TypeName = 'Hourly'")).Should().Be(1);
        (await db.CountAsync("TypeName = 'OneOff'")).Should().Be(2, "one-off jobs are not recurring runs");
        await store.UpsertRecurringAsync("Hourly", s_payload, first.AddHours(2), Hourly, 3, default);
        (await db.CountAsync("TypeName = 'Hourly'")).Should().Be(1);
    }

    /// <summary>The first <c>count</c> migrations of a source, under its name, as an older release shipped them.</summary>
    private sealed class FirstMigrations(IMigrationSource source, int count) : IMigrationSource
    {
        public string Name => source.Name;

        public IReadOnlyList<Migration> GetMigrations() => source.GetMigrations().Take(count).ToList();
    }
}
