using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// The store against a real SQL Server, which is the only way to prove the
/// OFFSET/FETCH paging spelling, the guarded DDL, and the claim actually work.
/// </summary>
/// <remarks>
/// The claim is why this file exists rather than trusting the SQLite suite.
/// SQL Server cannot return a set from a multi-row UPDATE, so the claim
/// mechanism itself differs from what shipped, and exclusivity under contention
/// is a property of the engine locking rather than of the C#.
/// </remarks>
public sealed class SqlServerJobStoreTests : IAsyncLifetime
{
    private static readonly byte[] s_payload = [1, 2, 3];

    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        var conn = await ConnectAsync().ConfigureAwait(false);
        await using (conn.ConfigureAwait(false))
        {
            await new MigrationRunner(conn, SchedulingOrmMigrations.SqlServer, new SqlServerMigrationDialect())
                .RunAsync(default).ConfigureAwait(false);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    private async Task<IAsyncDbConnection> ConnectAsync()
    {
        var raw = new SqlConnection(_container.GetConnectionString());
        var c = raw.AsAsync();
        await c.OpenAsync().ConfigureAwait(false);
        return c;
    }

    private async Task<OrmJobStore> StoreAsync()
        => new(await ConnectAsync().ConfigureAwait(false), OrmSchedulingDialect.SqlServer);

    [Fact]
    public async Task Enqueue_Then_Fetch_Round_Trips_On_SqlServer()
    {
        var store = await StoreAsync();

        await store.EnqueueAsync("Sql.Job", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var claimed = await store.FetchPendingAsync(10, default);

        claimed.Should().ContainSingle();
        claimed[0].TypeName.Should().Be("Sql.Job");
        claimed[0].Payload.Should().Equal(s_payload);
        claimed[0].Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task Claim_Honours_BatchSize_Through_Offset_Fetch()
    {
        // The whole reason this adapter has two query variants: SQL Server has no
        // LIMIT, so the batch bound is expressed as OFFSET 0 ROWS FETCH NEXT.
        var store = await StoreAsync();
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 7; i++)
        {
            await store.EnqueueAsync($"Batch.{i}", s_payload, now.AddSeconds(i), 3, null, default);
        }

        var batch = await store.FetchPendingAsync(3, default);

        batch.Should().HaveCount(3);
        batch.Select(j => j.TypeName).Should().Equal("Batch.0", "Batch.1", "Batch.2");
    }

    [Fact]
    public async Task Fetch_Ignores_Jobs_Scheduled_In_The_Future_On_SqlServer()
    {
        // DATETIMEOFFSET round-trip: a comparison that silently lost the offset
        // would make future jobs look due.
        var store = await StoreAsync();
        await store.EnqueueAsync("Later", s_payload, DateTimeOffset.UtcNow.AddHours(1), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Claimed_Job_Is_Not_Handed_Out_Again_On_SqlServer()
    {
        var store = await StoreAsync();
        await store.EnqueueAsync("Once", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().ContainSingle();
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Two_Competing_Pollers_Never_Claim_The_Same_Job_On_SqlServer()
    {
        // Not inferred from the SQLite result. Both read back by token, but
        // whether two overlapping UPDATEs can each take a row is the engine
        // locking, not the adapter. Concurrent, not sequential: run one after
        // the other and the second simply finds nothing claimable.
        var store = await StoreAsync();
        const int jobs = 40;
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < jobs; i++)
        {
            await store.EnqueueAsync($"Race.{i}", s_payload, now.AddSeconds(i), 3, null, default);
        }

        var a = await StoreAsync();
        var b = await StoreAsync();

        var results = await Task.WhenAll(
            Task.Run(() => a.FetchPendingAsync(jobs, default).AsTask()),
            Task.Run(() => b.FetchPendingAsync(jobs, default).AsTask()));

        var idsA = results[0].Select(j => j.Id).ToHashSet();
        var idsB = results[1].Select(j => j.Id).ToHashSet();

        idsA.Overlaps(idsB).Should().BeFalse("a job claimed by one poller must not be claimed by the other");
        (idsA.Count + idsB.Count).Should().Be(jobs, "every job should be claimed exactly once");
    }

    [Fact]
    public async Task MarkFailed_Makes_The_Job_Claimable_Again_On_SqlServer()
    {
        var store = await StoreAsync();
        await store.EnqueueAsync("Retry", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var job = (await store.FetchPendingAsync(1, default))[0];

        await store.MarkFailedAsync(job.Id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), default);

        var again = await store.FetchPendingAsync(1, default);
        again.Should().ContainSingle();
        again[0].Id.Should().Be(job.Id);
        again[0].Attempts.Should().Be(1);
    }

    [Fact]
    public async Task UpsertRecurring_Creates_Then_Updates_On_SqlServer()
    {
        // Exercises the other bounded query: the recurring lookup, whose
        // one-row bound is also spelled OFFSET/FETCH here.
        var store = await StoreAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);

        await store.UpsertRecurringAsync("Cron.Job", s_payload, at, "* * * * *", 3, default);
        await store.UpsertRecurringAsync("Cron.Job", [9, 9], at, "*/5 * * * *", 5, default);

        var claimed = await store.FetchPendingAsync(10, default);
        claimed.Should().ContainSingle("the second upsert must update rather than duplicate");
        claimed[0].CronExpression.Should().Be("*/5 * * * *");
        claimed[0].MaxAttempts.Should().Be(5);
        claimed[0].Payload.Should().Equal([9, 9]);
    }

    [Fact]
    public async Task DeadLetter_Removes_It_From_The_Queue_On_SqlServer()
    {
        var store = await StoreAsync();
        await store.EnqueueAsync("Bad", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 1, null, default);
        var job = (await store.FetchPendingAsync(1, default))[0];

        await store.DeadLetterAsync(job.Id, "boom", default);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Migrations_Are_Idempotent_On_SqlServer()
    {
        // The DDL is guarded by OBJECT_ID / COL_LENGTH / sys.indexes rather than
        // IF NOT EXISTS, which SQL Server does not have for these. A second run
        // applies nothing, but a broken guard would throw rather than no-op.
        var conn = await ConnectAsync();
        await using (conn.ConfigureAwait(false))
        {
            var applied = await new MigrationRunner(
                conn, SchedulingOrmMigrations.SqlServer, new SqlServerMigrationDialect())
                .RunAsync(default);

            applied.Should().BeEmpty("the fixture already migrated this database");
        }
    }
}
