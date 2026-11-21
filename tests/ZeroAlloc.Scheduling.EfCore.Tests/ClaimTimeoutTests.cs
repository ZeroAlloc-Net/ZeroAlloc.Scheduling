using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Issue #600: a run left Running is claimed again after <see cref="SchedulingOptions.ClaimTimeout"/>,
/// a late outcome of the abandoned run is ignored, and a dead-lettered recurring occurrence keeps the
/// schedule going.
/// </summary>
public sealed class ClaimTimeoutTests : IDisposable
{
    private const string Hourly = "0 * * * *";
    private const string Yearly = "0 0 1 1 *";
    private static readonly TimeSpan ShortClaimTimeout = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan ExpiredClaimDelay = TimeSpan.FromMilliseconds(50);

    private readonly string _connectionString = $"Data Source={Path.GetTempFileName()}";
    private readonly List<SchedulingDbContext> _contexts = [];

    public ClaimTimeoutTests()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();
    }

    [Fact]
    public async Task A_Running_Claim_Older_Than_The_Claim_Timeout_Is_Claimed_Again()
    {
        var abandoned = Store();
        await abandoned.EnqueueAsync("Abandoned", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var run = (await abandoned.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        (await Store(TimeSpan.FromHours(1)).FetchPendingAsync(10, default))
            .Should().BeEmpty("the claim is newer than the timeout");

        await Task.Delay(ExpiredClaimDelay);
        var retaken = (await Store(ShortClaimTimeout).FetchPendingAsync(10, default))
            .Should().ContainSingle("the claim is older than the timeout").Subject;

        retaken.Id.Should().Be(run.Id);
        retaken.Status.Should().Be(JobStatus.Running);
        retaken.Attempts.Should().Be(run.Attempts + 1, "the abandoned run counts as an attempt");
    }

    [Fact]
    public async Task The_Longest_Claim_Timeout_Never_Expires_A_Claim()
    {
        var store = Store(TimeSpan.MaxValue);
        await store.EnqueueAsync("Job", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().ContainSingle();
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Requeued_Dead_Letter_Starts_Its_Attempts_Again()
    {
        var store = Store(ShortClaimTimeout);
        await store.EnqueueAsync("Job", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 1, null, default);
        (await store.FetchPendingAsync(10, default)).Should().ContainSingle();
        await Task.Delay(ExpiredClaimDelay);
        var retaken = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;
        await store.DeadLetterAsync(retaken.Id, "abandoned", default);

        await store.RequeueAsync(retaken.Id);

        var requeued = (await ReadAllAsync()).Should().ContainSingle().Subject;
        requeued.Status.Should().Be(JobStatus.Pending);
        requeued.Attempts.Should().Be(0, "a requeued job gets its attempts back, or the worker dead-letters it again unrun");
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.DeadLetter)]
    public async Task A_Late_Outcome_Of_An_Abandoned_Claim_Does_Not_Overwrite_The_New_Run(JobStatus outcome)
    {
        var abandoned = Store();
        await abandoned.UpsertRecurringAsync("Hourly", [1], DateTimeOffset.UtcNow.AddSeconds(-1), Hourly, 3, default);
        var run = (await abandoned.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        await Task.Delay(ExpiredClaimDelay);
        var reclaimer = Store(ShortClaimTimeout);
        var retaken = (await reclaimer.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        var nextRun = DateTimeOffset.UtcNow.AddHours(1);
        switch (outcome)
        {
            case JobStatus.Succeeded:
                await abandoned.MarkSucceededAsync(run.Id, nextRun, Hourly, 3, default);
                break;
            case JobStatus.Failed:
                await abandoned.MarkFailedAsync(run.Id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), default);
                break;
            default:
                await abandoned.DeadLetterAsync(run.Id, "late", default);
                break;
        }

        var rows = await ReadAllAsync();
        rows.Should().ContainSingle("the late outcome adds no next occurrence");
        rows[0].Status.Should().Be(JobStatus.Running, "the run that took the claim back is still running");
        rows[0].Attempts.Should().Be(retaken.Attempts);
        rows[0].Error.Should().BeNull();

        await reclaimer.MarkSucceededAsync(retaken.Id, nextRun, Hourly, 3, default);
        rows = await ReadAllAsync();
        rows.Should().ContainSingle(r => r.Status == JobStatus.Succeeded);
        rows.Should().ContainSingle(r => r.Status == JobStatus.Pending);
    }

    [Fact]
    public async Task A_Recurring_Job_That_Always_Fails_Is_Dead_Lettered_And_Keeps_Its_Schedule()
    {
        await Store().UpsertRecurringAsync("Failing", [1], DateTimeOffset.UtcNow.AddSeconds(-1), Yearly, 2, default);
        var executor = new AlwaysFailingExecutor("Failing");

        var services = new ServiceCollection()
            .AddSingleton<IJobTypeExecutor>(executor)
            .AddScheduling(o =>
            {
                o.PollingInterval = TimeSpan.FromMilliseconds(20);
                o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            })
            .WithEfCore(o => o.UseSqlite(_connectionString))
            .Services.BuildServiceProvider();
        await using (services)
        {
            var worker = services.GetRequiredService<SchedulingWorkerService>();
            await worker.StartAsync(default);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            // The worker inserts the next occurrence right after the dead-letter; stopping it in
            // between would cancel that insert, so wait for both.
            while (!await HasDeadLetterAndSuccessorAsync() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            await worker.StopAsync(default);
        }

        executor.Calls.Should().Be(2, "retries of the occurrence stay bounded by its maximum attempts");
        var rows = await ReadAllAsync();
        rows.Should().ContainSingle(r => r.Status == JobStatus.DeadLetter && r.Error != null,
            "the dead-lettered occurrence keeps its error");
        var next = rows.Should().ContainSingle(r => r.Status == JobStatus.Pending, "the next occurrence is scheduled").Subject;
        next.CronExpression.Should().Be(Yearly);
        next.Attempts.Should().Be(0);
        next.ScheduledAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    private SchedulingDbContext NewContext()
    {
        var context = new SchedulingDbContext(
            new DbContextOptionsBuilder<SchedulingDbContext>().UseSqlite(_connectionString).Options);
        _contexts.Add(context);
        return context;
    }

    private EfCoreJobStore Store(TimeSpan? claimTimeout = null)
        => claimTimeout is { } timeout
            ? new EfCoreJobStore(NewContext(), new FixedOptions(new SchedulingOptions { ClaimTimeout = timeout }))
            : new EfCoreJobStore(NewContext());

    private async Task<List<JobEntryEntity>> ReadAllAsync()
    {
        using var db = new SchedulingDbContext(
            new DbContextOptionsBuilder<SchedulingDbContext>().UseSqlite(_connectionString).Options);
        return await db.Jobs.AsNoTracking().ToListAsync().ConfigureAwait(false);
    }

    private async Task<bool> HasDeadLetterAndSuccessorAsync()
    {
        using var db = new SchedulingDbContext(
            new DbContextOptionsBuilder<SchedulingDbContext>().UseSqlite(_connectionString).Options);
        var hasDeadLetter = await db.Jobs.AnyAsync(j => j.Status == JobStatus.DeadLetter).ConfigureAwait(false);
        return hasDeadLetter
            && await db.Jobs.AnyAsync(j => j.Status == JobStatus.Pending && j.Attempts == 0).ConfigureAwait(false);
    }

    private sealed class FixedOptions(SchedulingOptions value) : IOptionsMonitor<SchedulingOptions>
    {
        public SchedulingOptions CurrentValue => value;

        public SchedulingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SchedulingOptions, string?> listener) => null;
    }

    private sealed class AlwaysFailingExecutor(string typeName) : IJobTypeExecutor
    {
        private int _calls;

        public string TypeName => typeName;

        public int MaxAttempts => 0;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("The job fails on every attempt.");
        }
    }
}
