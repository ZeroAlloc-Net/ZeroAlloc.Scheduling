using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// The upgrade path for a database created before the claim token existed.
/// </summary>
/// <remarks>
/// The fixture migrates from empty, so it applies versions 1 and 2 back to back
/// against a table it just created. That is not the path an existing deployment
/// takes: theirs has a version-1 table with rows in it, and only version 2 to
/// apply. The ALTER has to land on populated data, and jobs enqueued before the
/// upgrade have to stay claimable afterwards.
/// </remarks>
public sealed class ClaimTokenUpgradeTests
{
    private static readonly byte[] s_payload = [7, 7, 7];

    /// <summary>
    /// Version 1 exactly as it shipped, so the starting point is the real one
    /// rather than the current schema with a column removed.
    /// </summary>
    private sealed class V1Only : IMigrationSource
    {
        public IReadOnlyList<Migration> GetMigrations() =>
        [
            new Migration(1, "create_scheduling_jobs", """
                CREATE TABLE IF NOT EXISTS SchedulingJobs (
                    Id             BLOB NOT NULL PRIMARY KEY,
                    TypeName       TEXT NOT NULL,
                    Payload        BLOB NOT NULL,
                    Status         INTEGER NOT NULL,
                    Attempts       INTEGER NOT NULL,
                    MaxAttempts    INTEGER NOT NULL,
                    ScheduledAt    TEXT NOT NULL,
                    StartedAt      TEXT NULL,
                    CompletedAt    TEXT NULL,
                    NextRunAt      TEXT NULL,
                    CronExpression TEXT NULL,
                    Error          TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_SchedulingJobs_Status_ScheduledAt
                    ON SchedulingJobs (Status, ScheduledAt);
                """),
        ];
    }

    [Fact]
    public async Task A_Database_Created_Before_The_Claim_Token_Upgrades_And_Keeps_Its_Jobs()
    {
        await using var fx = new SqliteFixture();

        // Stand up the old schema and enqueue against it, so the ALTER lands on
        // a populated table rather than an empty one.
        var v1 = await fx.ConnectAsync();
        await using (v1.ConfigureAwait(false))
        {
            var applied = await new MigrationRunner(v1, new V1Only(), new SqliteMigrationDialect())
                .RunAsync(default);
            applied.Select(m => m.Version).Should().Equal(1);
        }

        var legacyStore = new OrmJobStore(await fx.ConnectAsync());
        await legacyStore.EnqueueAsync("Legacy.Job", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        // Now upgrade. Only version 2 is outstanding.
        var upgrade = await fx.ConnectAsync();
        await using (upgrade.ConfigureAwait(false))
        {
            var applied = await new MigrationRunner(
                upgrade, SchedulingOrmMigrations.Sqlite, new SqliteMigrationDialect()).RunAsync(default);

            // Version 1 is already recorded as applied, so only 2 is outstanding.
            applied.Select(m => m.Version).Should().Equal([2]);
        }

        // The job enqueued under the old schema has a NULL ClaimToken. It must
        // still be claimable, and must not come back under a second claim.
        var store = new OrmJobStore(await fx.ConnectAsync());
        var claimed = await store.FetchPendingAsync(10, default);

        claimed.Should().ContainSingle();
        claimed[0].TypeName.Should().Be("Legacy.Job");
        claimed[0].Payload.Should().Equal(s_payload);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Pre_Upgrade_Rows_With_Null_Tokens_Are_Never_Returned_By_A_Later_Claim()
    {
        // Guards the shape of the read-back: filtering on a token that happened
        // to be NULL would sweep in every legacy row at once.
        await using var fx = new SqliteFixture();
        await fx.MigrateAsync();

        var store = new OrmJobStore(await fx.ConnectAsync());
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        for (var i = 0; i < 5; i++)
        {
            await store.EnqueueAsync($"Job.{i}", s_payload, now.AddSeconds(i), 3, null, default);
        }

        // Every row still has a NULL ClaimToken at this point. A batch of two
        // must return two, not five.
        var claimed = await store.FetchPendingAsync(2, default);

        claimed.Should().HaveCount(2);
        claimed.Select(j => j.TypeName).Should().Equal("Job.0", "Job.1");
    }
}
