using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Scheduling.Tests;

public sealed class JobStatusFsmTests
{
    public static TheoryData<JobStatus, JobTrigger> AllPairs()
    {
        var data = new TheoryData<JobStatus, JobTrigger>();
        foreach (var status in Enum.GetValues<JobStatus>())
            foreach (var trigger in Enum.GetValues<JobTrigger>())
                data.Add(status, trigger);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void TryTransition_MatchesTryFire(JobStatus from, JobTrigger trigger)
    {
        var fsm = new JobStatusFsm(from);
        var expectedLegal = fsm.TryFire(trigger);

        var legal = JobStatusFsm.TryTransition(from, trigger, out var to);

        legal.Should().Be(expectedLegal);
        to.Should().Be(expectedLegal ? fsm.Current : from);
    }

    [Fact]
    public void TryTransition_DoesNotAllocate()
        => AllocationGate.AssertBudget(0, 1000, static () =>
        {
            JobStatusFsm.TryTransition(JobStatus.Pending, JobTrigger.Claim, out _);
            JobStatusFsm.TryTransition(JobStatus.Succeeded, JobTrigger.Claim, out _);
        }, "JobStatusFsm.TryTransition");
}
