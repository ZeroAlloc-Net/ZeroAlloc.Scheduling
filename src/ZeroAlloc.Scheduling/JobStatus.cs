namespace ZeroAlloc.Scheduling;

public enum JobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    DeadLetter,
}
