using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// Issue #600 on the in-memory store and the worker: a run left Running is claimed again after
/// <see cref="SchedulingOptions.ClaimTimeout"/>, and a dead-lettered recurring occurrence keeps the
/// schedule going.
/// </summary>
public sealed class ClaimTimeoutTests
{
    private const string Yearly = "0 0 1 1 *";
    private static readonly TimeSpan ShortClaimTimeout = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan ExpiredClaimDelay = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task A_Running_Claim_Older_Than_The_Claim_Timeout_Is_Claimed_Again()
    {
        var options = new SchedulingOptions { ClaimTimeout = TimeSpan.FromHours(1) };
        using var store = new InMemoryJobStore(new FixedOptions(options));
        await store.EnqueueAsync("Abandoned", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty("the claim is newer than the timeout");

        options.ClaimTimeout = ShortClaimTimeout;
        await Task.Delay(ExpiredClaimDelay);
        var retaken = (await store.FetchPendingAsync(10, default))
            .Should().ContainSingle("the claim is older than the timeout").Subject;

        retaken.Id.Should().Be(run.Id);
        retaken.Status.Should().Be(JobStatus.Running);
        retaken.Attempts.Should().Be(run.Attempts + 1, "the abandoned run counts as an attempt");

        options.ClaimTimeout = TimeSpan.FromHours(1);
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty("the new claim is fresh");
    }

    [Fact]
    public async Task An_Expired_Claim_Is_Taken_Before_Due_Jobs()
    {
        var options = new SchedulingOptions { ClaimTimeout = TimeSpan.FromHours(1) };
        using var store = new InMemoryJobStore(new FixedOptions(options));
        await store.EnqueueAsync("Abandoned", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        (await store.FetchPendingAsync(1, default)).Should().ContainSingle();
        await store.EnqueueAsync("Due", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        options.ClaimTimeout = ShortClaimTimeout;
        await Task.Delay(ExpiredClaimDelay);

        (await store.FetchPendingAsync(1, default)).Should().ContainSingle()
            .Which.TypeName.Should().Be("Abandoned", "a queue that always has due jobs must not starve an expired claim");
    }

    [Fact]
    public async Task The_Longest_Claim_Timeout_Never_Expires_A_Claim()
    {
        using var store = new InMemoryJobStore(new FixedOptions(new SchedulingOptions { ClaimTimeout = TimeSpan.MaxValue }));
        await store.EnqueueAsync("Job", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);

        (await store.FetchPendingAsync(10, default)).Should().ContainSingle();
        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Finished_Run_Is_Never_Claimed_Again()
    {
        using var store = new InMemoryJobStore(new FixedOptions(new SchedulingOptions { ClaimTimeout = ShortClaimTimeout }));
        await store.EnqueueAsync("Done", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;
        await store.MarkSucceededAsync(run.Id, null, null, 3, default);

        await Task.Delay(ExpiredClaimDelay);

        (await store.FetchPendingAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public void ClaimTimeout_Must_Be_Positive()
    {
        var options = new SchedulingOptions();

        options.ClaimTimeout.Should().Be(TimeSpan.FromMinutes(15));
        FluentActions.Invoking(() => options.ClaimTimeout = TimeSpan.Zero)
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Upsert_Adds_A_Run_Next_To_A_Dead_Lettered_One_But_Not_Next_To_A_Retrying_One()
    {
        const string Hourly = "0 * * * *";
        using var store = new InMemoryJobStore();
        await store.UpsertRecurringAsync("Hourly", [1], DateTimeOffset.UtcNow.AddSeconds(-1), Hourly, 3, default);
        var run = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;

        await store.MarkFailedAsync(run.Id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), default);
        await store.UpsertRecurringAsync("Hourly", [1], DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);
        store.AllEntries.Should().ContainSingle("the run waiting for its retry is live");

        var retry = (await store.FetchPendingAsync(10, default)).Should().ContainSingle().Subject;
        await store.DeadLetterAsync(retry.Id, "boom", default);
        await store.EnqueueAsync("Hourly", [1], DateTimeOffset.UtcNow.AddHours(1), 3, null, default);
        await store.UpsertRecurringAsync("Hourly", [1], DateTimeOffset.UtcNow.AddHours(1), Hourly, 3, default);

        store.AllEntries.Should().HaveCount(3);
        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.DeadLetter && e.Error == "boom",
            "the dead-lettered run is kept with its error");
        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.Pending && e.CronExpression == Hourly,
            "a one-off of the same type is not a live recurring run");
    }

    [Fact]
    public async Task A_Recurring_Job_That_Always_Fails_Is_Dead_Lettered_And_Keeps_Its_Schedule()
    {
        using var store = new InMemoryJobStore();
        await store.UpsertRecurringAsync("Failing", [1], DateTimeOffset.UtcNow.AddSeconds(-1), Yearly, 2, default);
        var executor = new AlwaysFailingExecutor("Failing");
        var worker = Worker(store, executor);

        for (var poll = 0; poll < 50 && !HasDeadLetter(store); poll++)
        {
            await worker.ProcessBatchAsync(default);
            await Task.Delay(5);
        }

        executor.Calls.Should().Be(2, "retries of the occurrence stay bounded by its maximum attempts");
        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.DeadLetter && e.Error != null,
            "the dead-lettered occurrence keeps its error");
        var next = store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.Pending,
            "the next occurrence is scheduled").Subject;
        next.CronExpression.Should().Be(Yearly);
        next.Attempts.Should().Be(0);
        next.ScheduledAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_Reclaimed_Run_Without_Attempts_Left_Is_Dead_Lettered_Without_Running()
    {
        using var store = new InMemoryJobStore(new FixedOptions(new SchedulingOptions { ClaimTimeout = ShortClaimTimeout }));
        await store.UpsertRecurringAsync("Failing", [1], DateTimeOffset.UtcNow.AddSeconds(-1), Yearly, 1, default);
        (await store.FetchPendingAsync(10, default)).Should().ContainSingle("the run that is then abandoned");
        await Task.Delay(ExpiredClaimDelay);

        var executor = new AlwaysFailingExecutor("Failing");
        await Worker(store, executor).ProcessBatchAsync(default);

        executor.Calls.Should().Be(0, "the abandoned run used the only attempt");
        store.AllEntries.Should().ContainSingle(e =>
            e.Status == JobStatus.DeadLetter && e.Error == SchedulingWorkerService.AbandonedClaimError);
        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.Pending && e.CronExpression == Yearly,
            "the next occurrence is scheduled");
    }

    [Fact]
    public async Task A_Reclaimed_Run_With_Attempts_Left_Runs_As_The_Next_Attempt()
    {
        using var store = new InMemoryJobStore(new FixedOptions(new SchedulingOptions { ClaimTimeout = ShortClaimTimeout }));
        await store.EnqueueAsync("Job", [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        (await store.FetchPendingAsync(10, default)).Should().ContainSingle("the run that is then abandoned");
        await Task.Delay(ExpiredClaimDelay);

        var attempts = new List<int>();
        var executor = new RecordingExecutor("Job", attempts);
        await Worker(store, executor).ProcessBatchAsync(default);

        attempts.Should().Equal(2);
        store.AllEntries.Should().ContainSingle(e => e.Status == JobStatus.Succeeded);
    }

    private static bool HasDeadLetter(InMemoryJobStore store)
    {
        foreach (var entry in store.AllEntries)
        {
            if (entry.Status == JobStatus.DeadLetter) return true;
        }

        return false;
    }

    private static SchedulingWorkerService Worker(InMemoryJobStore store, IJobTypeExecutor executor)
        => new ServiceCollection()
            .AddSingleton<IJobStore>(store)
            .AddSingleton(executor)
            .AddScheduling(o => o.RetryBaseDelay = TimeSpan.FromMilliseconds(1))
            .Services.BuildServiceProvider()
            .GetRequiredService<SchedulingWorkerService>();

    private sealed class FixedOptions(SchedulingOptions value) : IOptionsMonitor<SchedulingOptions>
    {
        public SchedulingOptions CurrentValue => value;

        public SchedulingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SchedulingOptions, string?> listener) => null;
    }

    private sealed class AlwaysFailingExecutor(string typeName) : IJobTypeExecutor
    {
        public string TypeName => typeName;

        public int MaxAttempts => 0;

        public int Calls { get; private set; }

        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("The job fails on every attempt.");
        }
    }

    private sealed class RecordingExecutor(string typeName, List<int> attempts) : IJobTypeExecutor
    {
        public string TypeName => typeName;

        public int MaxAttempts => 0;

        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
        {
            attempts.Add(ctx.Attempt);
            return ValueTask.CompletedTask;
        }
    }
}
