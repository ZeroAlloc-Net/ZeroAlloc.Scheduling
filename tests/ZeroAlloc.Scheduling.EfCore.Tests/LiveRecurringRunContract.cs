using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Issue #604: a recurring job keeps one live run, Pending, Running or Failed, on EF Core. The
/// filtered unique index <c>UX_ScheduledJobs_LiveRecurring</c> enforces it, and the store skips a
/// live run another caller inserted instead of adding a second one or failing.
/// </summary>
/// <remarks>
/// The index filter is raw SQL, so every provider the store supports runs these: SQLite, and a real
/// PostgreSQL and SQL Server, whose identifier quoting and filtered indexes differ.
/// </remarks>
public abstract class LiveRecurringRunContract
{
    private const string Hourly = "0 * * * *";

    /// <summary>Options for a new, empty database whose schema <c>EnsureCreated</c> has made.</summary>
    protected abstract Task<DbContextOptions<SchedulingDbContext>> NewDatabaseAsync();

    [Fact]
    public async Task The_Schema_Rejects_A_Second_Live_Run_Of_A_Recurring_Job()
    {
        var options = await NewDatabaseAsync();
        await using var db = new SchedulingDbContext(options);
        db.Jobs.Add(Run("Job", JobStatus.Pending, Hourly));
        await db.SaveChangesAsync();

        db.Jobs.Add(Run("Job", JobStatus.Failed, Hourly));
        var second = () => db.SaveChangesAsync();

        await second.Should().ThrowAsync<DbUpdateException>("a job may have one live run only");
    }

    [Fact]
    public async Task The_Schema_Allows_Finished_Runs_Other_Jobs_And_One_Offs_Next_To_A_Live_Run()
    {
        var options = await NewDatabaseAsync();
        await using var db = new SchedulingDbContext(options);
        db.Jobs.AddRange(
            Run("Job", JobStatus.Running, Hourly),
            Run("Job", JobStatus.Succeeded, Hourly),
            Run("Job", JobStatus.Succeeded, Hourly),
            Run("Job", JobStatus.DeadLetter, Hourly),
            Run("Other", JobStatus.Pending, Hourly),
            Run("Job", JobStatus.Pending, cron: null),
            Run("Job", JobStatus.Pending, cron: null));

        await db.SaveChangesAsync();

        (await db.Jobs.CountAsync()).Should().Be(7);
    }

    [Fact]
    public async Task Concurrent_Seeds_Leave_One_Live_Run()
    {
        var options = await NewDatabaseAsync();
        const int seeds = 8;
        var contexts = Enumerable.Range(0, seeds).Select(_ => new SchedulingDbContext(options)).ToList();
        try
        {
            await Task.WhenAll(contexts.Select(db => Task.Run(() =>
                new EfCoreJobStore(db).UpsertRecurringAsync("Job", [1], DateTimeOffset.UtcNow, Hourly, 3, default).AsTask())));
        }
        finally
        {
            foreach (var db in contexts)
                await db.DisposeAsync();
        }

        (await LiveRunsAsync(options, "Job")).Should().Be(1);
    }

    [Fact]
    public async Task A_Seed_Skips_A_Live_Run_Inserted_After_Its_Check()
    {
        // The competing seed inserts once the store's check has run and released its connection,
        // so the store's own insert is the one the index rejects.
        var plain = await NewDatabaseAsync();
        var competing = new CompetingSeed(plain);
        var options = new DbContextOptionsBuilder<SchedulingDbContext>(plain).AddInterceptors(competing).Options;

        await using (var db = new SchedulingDbContext(options))
        {
            await new EfCoreJobStore(db).UpsertRecurringAsync("Job", [1], DateTimeOffset.UtcNow, Hourly, 3, default);
            db.ChangeTracker.Entries().Should().BeEmpty("the rejected run must not stay tracked");
        }

        competing.Inserted.Should().BeTrue("the test must have raced the store's insert");
        (await LiveRunsAsync(plain, "Job")).Should().Be(1);
    }

    [Fact]
    public async Task A_Successor_Skips_A_Live_Run_That_Already_Exists()
    {
        // An outcome for a run this store did not claim is recorded unchecked, so completing a
        // dead-lettered run here adds a successor next to the job's live run.
        var options = await NewDatabaseAsync();
        var finished = Run("Job", JobStatus.DeadLetter, Hourly);
        var live = Run("Job", JobStatus.Pending, Hourly);
        await using (var seed = new SchedulingDbContext(options))
        {
            seed.Jobs.AddRange(finished, live);
            await seed.SaveChangesAsync();
        }

        await using (var db = new SchedulingDbContext(options))
        {
            await new EfCoreJobStore(db).MarkSucceededAsync(
                finished.Id, DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);
        }

        await using var check = new SchedulingDbContext(options);
        (await check.Jobs.SingleAsync(j => j.Id == finished.Id)).Status.Should().Be(JobStatus.Succeeded);
        (await check.Jobs.Where(j => j.CronExpression != null
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running || j.Status == JobStatus.Failed))
            .Select(j => j.Id).ToListAsync())
            .Should().Equal(live.Id);
    }

    [Fact]
    public async Task A_Failed_Insert_That_Is_No_Conflict_Still_Throws()
    {
        // Only a recurring run the index rejects for a live run is skipped. Here the NOT NULL
        // constraint on Payload rejects it, no live run exists, and the error reaches the caller.
        var options = await NewDatabaseAsync();
        await using var db = new SchedulingDbContext(options);
        var store = new EfCoreJobStore(db);

        var upsert = () => store.UpsertRecurringAsync("Job", null!, DateTimeOffset.UtcNow, Hourly, 3, default).AsTask();

        await upsert.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Entries().Should().BeEmpty("the rejected run must not stay tracked");
    }

    [Fact]
    public async Task A_Conflict_Saved_With_Other_Changes_Is_Not_Skipped()
    {
        // The context also tracks a change of the caller's own. The failed save did not write it,
        // so the store must not report success, even though the job has a live run.
        var plain = await NewDatabaseAsync();
        var options = new DbContextOptionsBuilder<SchedulingDbContext>(plain).AddInterceptors(new CompetingSeed(plain)).Options;
        await using var db = new SchedulingDbContext(options);
        db.Jobs.Add(Run("Other", JobStatus.Pending, cron: null));

        var upsert = () => new EfCoreJobStore(db).UpsertRecurringAsync("Job", [1], DateTimeOffset.UtcNow, Hourly, 3, default).AsTask();

        await upsert.Should().ThrowAsync<DbUpdateException>();
        (await LiveRunsAsync(plain, "Job")).Should().Be(1);
    }

    [Fact]
    public async Task Requeuing_A_Finished_Run_Next_To_A_Live_Run_Reruns_It_Once()
    {
        var options = await NewDatabaseAsync();
        var deadLettered = Run("Job", JobStatus.DeadLetter, Hourly);
        var next = Run("Job", JobStatus.Pending, Hourly);
        await using (var seed = new SchedulingDbContext(options))
        {
            seed.Jobs.AddRange(deadLettered, next);
            await seed.SaveChangesAsync();
        }

        await using (var db = new SchedulingDbContext(options))
            await new EfCoreJobStore(db).RequeueAsync(deadLettered.Id);

        await using var check = new SchedulingDbContext(options);
        var requeued = await check.Jobs.SingleAsync(j => j.Id == deadLettered.Id);
        requeued.Status.Should().Be(JobStatus.Pending);
        requeued.CronExpression.Should().BeNull("the schedule stays with the live run");
        (await check.Jobs.SingleAsync(j => j.Id == next.Id)).CronExpression.Should().Be(Hourly);
        (await LiveRunsAsync(options, "Job")).Should().Be(1);
    }

    [Fact]
    public async Task Requeuing_A_Finished_Run_Without_A_Live_Run_Keeps_Its_Schedule()
    {
        var options = await NewDatabaseAsync();
        var deadLettered = Run("Job", JobStatus.DeadLetter, Hourly);
        await using (var seed = new SchedulingDbContext(options))
        {
            seed.Jobs.Add(deadLettered);
            await seed.SaveChangesAsync();
        }

        await using (var db = new SchedulingDbContext(options))
            await new EfCoreJobStore(db).RequeueAsync(deadLettered.Id);

        await using var check = new SchedulingDbContext(options);
        var requeued = await check.Jobs.SingleAsync(j => j.Id == deadLettered.Id);
        requeued.Status.Should().Be(JobStatus.Pending);
        requeued.CronExpression.Should().Be(Hourly);
    }

    [Fact]
    public async Task A_Requeue_Retried_After_A_Lost_Response_Leaves_A_Claimed_Run_Alone()
    {
        // The first update commits, its response is lost, and a worker claims the run before the
        // store retries. The retry must not reset that claim.
        var plain = await NewDatabaseAsync();
        var deadLettered = Run("Job", JobStatus.DeadLetter, Hourly);
        await using (var seed = new SchedulingDbContext(plain))
        {
            seed.Jobs.Add(deadLettered);
            await seed.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<SchedulingDbContext>(plain)
            .AddInterceptors(new LostResponseThenClaim(plain, deadLettered.Id)).Options;
        await using (var db = new SchedulingDbContext(options))
            await new EfCoreJobStore(db).RequeueAsync(deadLettered.Id);

        await using var check = new SchedulingDbContext(plain);
        var run = await check.Jobs.SingleAsync(j => j.Id == deadLettered.Id);
        run.Status.Should().Be(JobStatus.Running);
        run.Attempts.Should().Be(1);
    }

    private static async Task<int> LiveRunsAsync(DbContextOptions<SchedulingDbContext> options, string typeName)
    {
        var db = new SchedulingDbContext(options);
        await using (db.ConfigureAwait(false))
        {
            return await db.Jobs.CountAsync(j => j.TypeName == typeName && j.CronExpression != null
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running || j.Status == JobStatus.Failed)).ConfigureAwait(false);
        }
    }

    private static JobEntryEntity Run(string typeName, JobStatus status, string? cron) => new()
    {
        Id = JobId.New(), TypeName = typeName, Payload = [1], Status = status,
        MaxAttempts = 3, ScheduledAt = DateTimeOffset.UtcNow, CronExpression = cron,
    };

    private sealed class CompetingSeed(DbContextOptions<SchedulingDbContext> options) : DbConnectionInterceptor
    {
        public bool Inserted { get; private set; }

        public override async Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
        {
            if (Inserted)
                return;

            Inserted = true;
            var db = new SchedulingDbContext(options);
            await using (db.ConfigureAwait(false))
            {
                db.Jobs.Add(Run("Job", JobStatus.Pending, Hourly));
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }

    // Lets the first update commit, then claims the run as a worker would and reports a
    // failure, as a connection that drops before the response arrives does.
    private sealed class LostResponseThenClaim(DbContextOptions<SchedulingDbContext> options, JobId id) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_done)
                return result;

            _done = true;
            var db = new SchedulingDbContext(options);
            await using (db.ConfigureAwait(false))
            {
                await db.Jobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Running)
                    .SetProperty(j => j.Attempts, 1), cancellationToken).ConfigureAwait(false);
            }

            throw new LostResponseException();
        }
    }

    private sealed class LostResponseException() : DbException("The connection dropped before the response arrived.");
}
