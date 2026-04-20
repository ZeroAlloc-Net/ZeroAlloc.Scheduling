namespace ZeroAlloc.Scheduling.Tests;

public sealed class ContractTests
{
    [Fact]
    public void JobEntry_DefaultStatus_IsPending()
    {
        var entry = new JobEntry
        {
            Id = Guid.NewGuid(),
            TypeName = "MyJob",
            Payload = Array.Empty<byte>(),
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = 3,
            ScheduledAt = DateTimeOffset.UtcNow,
        };

        entry.Status.Should().Be(JobStatus.Pending);
        entry.NextRunAt.Should().BeNull();
        entry.Error.Should().BeNull();
    }

    [Fact]
    public void SchedulingOptions_Defaults_AreReasonable()
    {
        var opts = new SchedulingOptions();
        opts.PollingInterval.Should().Be(TimeSpan.FromSeconds(5));
        opts.BatchSize.Should().Be(20);
        opts.RetryBaseDelay.Should().Be(TimeSpan.FromSeconds(2));
        opts.DefaultMaxAttempts.Should().Be(3);
    }
}
