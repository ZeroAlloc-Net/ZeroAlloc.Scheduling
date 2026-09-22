using Microsoft.EntityFrameworkCore;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Regression for #224 — two pollers running concurrently were each handed the
/// other's jobs as well as their own, so every job ran twice.
/// </summary>
/// <remarks>
/// These must stay concurrent. Run the pollers one after the other and the
/// second simply finds nothing claimable, so a sequential version of this test
/// passes against the broken implementation and proves nothing.
/// </remarks>
public sealed class ConcurrentClaimTests
{
    private static DbContextOptions<SchedulingDbContext> NewDatabase()
        => new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlite($"Data Source={Path.GetTempFileName()}")
            .Options;

    [Fact]
    public async Task Two_Concurrent_Pollers_Never_Claim_The_Same_Job()
    {
        var opts = NewDatabase();
        using var seedDb = new SchedulingDbContext(opts);
        seedDb.Database.EnsureCreated();

        const int jobs = 40;
        var due = DateTimeOffset.UtcNow.AddSeconds(-1);
        var seed = new EfCoreJobStore(seedDb);
        for (var i = 0; i < jobs; i++)
            await seed.EnqueueAsync($"Job.{i}", [1], due, 3, null, default);

        using var dbA = new SchedulingDbContext(opts);
        using var dbB = new SchedulingDbContext(opts);
        var a = new EfCoreJobStore(dbA);
        var b = new EfCoreJobStore(dbB);

        var results = await Task.WhenAll(
            Task.Run(() => a.FetchPendingAsync(jobs, default).AsTask()),
            Task.Run(() => b.FetchPendingAsync(jobs, default).AsTask()));

        var idsA = results[0].Select(j => j.Id).ToHashSet();
        var idsB = results[1].Select(j => j.Id).ToHashSet();

        idsA.Overlaps(idsB).Should()
            .BeFalse($"a job claimed by one poller must not be handed to the other (A={idsA.Count}, B={idsB.Count})");
        (idsA.Count + idsB.Count).Should().Be(jobs, "every due job should be claimed exactly once");
    }

    [Fact]
    public async Task Concurrent_Pollers_Share_A_Batch_Without_Duplication()
    {
        // Batch smaller than the backlog, so both pollers genuinely compete for
        // the same head of the queue rather than one taking everything.
        var opts = NewDatabase();
        using var seedDb = new SchedulingDbContext(opts);
        seedDb.Database.EnsureCreated();

        const int jobs = 40;
        var due = DateTimeOffset.UtcNow.AddSeconds(-1);
        var seed = new EfCoreJobStore(seedDb);
        for (var i = 0; i < jobs; i++)
            await seed.EnqueueAsync($"Job.{i}", [1], due, 3, null, default);

        using var dbA = new SchedulingDbContext(opts);
        using var dbB = new SchedulingDbContext(opts);
        var a = new EfCoreJobStore(dbA);
        var b = new EfCoreJobStore(dbB);

        var results = await Task.WhenAll(
            Task.Run(() => a.FetchPendingAsync(10, default).AsTask()),
            Task.Run(() => b.FetchPendingAsync(10, default).AsTask()));

        var idsA = results[0].Select(j => j.Id).ToHashSet();
        var idsB = results[1].Select(j => j.Id).ToHashSet();

        idsA.Overlaps(idsB).Should().BeFalse("claims must be exclusive even when batches overlap");
    }
}
