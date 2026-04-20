namespace ZeroAlloc.Scheduling;

public sealed record JobSummary(int Pending, int Running, int Succeeded, int Failed, int DeadLetter);
