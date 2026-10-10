namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>Fixed jobs, and the exact JSON the dashboard API writes for each of them.</summary>
internal static class SampleJobs
{
    public const string SucceededId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    public const string PendingId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    public const string RunningId = "01ARZ3NDEKTSV4RRFFQ69G5FAX";
    public const string DeadLetterId = "01ARZ3NDEKTSV4RRFFQ69G5FAY";
    public const string FailedId = "01ARZ3NDEKTSV4RRFFQ69G5FAZ";

    public const string SucceededJson =
        "{\"id\":\"01ARZ3NDEKTSV4RRFFQ69G5FAV\",\"typeName\":\"Sample.Report\",\"payload\":\"AQID\","
        + "\"status\":\"Succeeded\",\"attempts\":2,\"maxAttempts\":5,"
        + "\"scheduledAt\":\"2026-01-02T03:04:05+00:00\",\"startedAt\":\"2026-01-02T03:04:06+00:00\","
        + "\"completedAt\":\"2026-01-02T03:04:07+00:00\",\"nextRunAt\":\"2026-01-03T03:04:05+00:00\","
        + "\"cronExpression\":\"0 * * * *\",\"error\":null}";

    public const string PendingJson =
        "{\"id\":\"01ARZ3NDEKTSV4RRFFQ69G5FAW\",\"typeName\":\"Sample.Pending\",\"payload\":\"\","
        + "\"status\":\"Pending\",\"attempts\":0,\"maxAttempts\":3,"
        + "\"scheduledAt\":\"2026-01-02T03:04:05+00:00\",\"startedAt\":null,"
        + "\"completedAt\":null,\"nextRunAt\":null,\"cronExpression\":null,\"error\":null}";

    public const string RunningJson =
        "{\"id\":\"01ARZ3NDEKTSV4RRFFQ69G5FAX\",\"typeName\":\"Sample.Running\",\"payload\":\"BA==\","
        + "\"status\":\"Running\",\"attempts\":1,\"maxAttempts\":3,"
        + "\"scheduledAt\":\"2026-01-02T03:04:05+00:00\",\"startedAt\":\"2026-01-02T03:04:06+00:00\","
        + "\"completedAt\":null,\"nextRunAt\":null,\"cronExpression\":null,\"error\":null}";

    public const string DeadLetterJson =
        "{\"id\":\"01ARZ3NDEKTSV4RRFFQ69G5FAY\",\"typeName\":\"Sample.Dead\",\"payload\":\"\","
        + "\"status\":\"DeadLetter\",\"attempts\":3,\"maxAttempts\":3,"
        + "\"scheduledAt\":\"2026-01-02T03:04:05+00:00\",\"startedAt\":\"2026-01-02T03:04:06+00:00\","
        + "\"completedAt\":\"2026-01-02T03:04:07+00:00\",\"nextRunAt\":null,\"cronExpression\":null,"
        + "\"error\":\"gave up\"}";

    public const string FailedJson =
        "{\"id\":\"01ARZ3NDEKTSV4RRFFQ69G5FAZ\",\"typeName\":\"Sample.Failed\",\"payload\":\"\","
        + "\"status\":\"Failed\",\"attempts\":1,\"maxAttempts\":3,"
        + "\"scheduledAt\":\"2026-01-02T03:04:05+00:00\",\"startedAt\":\"2026-01-02T03:04:06+00:00\","
        + "\"completedAt\":null,\"nextRunAt\":\"2026-01-02T03:09:05+00:00\",\"cronExpression\":null,"
        + "\"error\":\"timeout\"}";

    public const string SummaryJson = "{\"pending\":1,\"running\":2,\"succeeded\":3,\"failed\":4,\"deadLetter\":5}";

    private static readonly DateTimeOffset Scheduled = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static JobEntry Succeeded { get; } = new()
    {
        Id = JobId.Parse(SucceededId), TypeName = "Sample.Report", Payload = [1, 2, 3],
        Status = JobStatus.Succeeded, Attempts = 2, MaxAttempts = 5, ScheduledAt = Scheduled,
        StartedAt = Scheduled.AddSeconds(1), CompletedAt = Scheduled.AddSeconds(2),
        NextRunAt = Scheduled.AddDays(1), CronExpression = "0 * * * *",
    };

    public static JobEntry Pending { get; } = new()
    {
        Id = JobId.Parse(PendingId), TypeName = "Sample.Pending", Payload = [],
        Status = JobStatus.Pending, Attempts = 0, MaxAttempts = 3, ScheduledAt = Scheduled,
    };

    public static JobEntry Running { get; } = new()
    {
        Id = JobId.Parse(RunningId), TypeName = "Sample.Running", Payload = [4],
        Status = JobStatus.Running, Attempts = 1, MaxAttempts = 3, ScheduledAt = Scheduled,
        StartedAt = Scheduled.AddSeconds(1),
    };

    public static JobEntry DeadLetter { get; } = new()
    {
        Id = JobId.Parse(DeadLetterId), TypeName = "Sample.Dead", Payload = [],
        Status = JobStatus.DeadLetter, Attempts = 3, MaxAttempts = 3, ScheduledAt = Scheduled,
        StartedAt = Scheduled.AddSeconds(1), CompletedAt = Scheduled.AddSeconds(2), Error = "gave up",
    };

    public static JobEntry Failed { get; } = new()
    {
        Id = JobId.Parse(FailedId), TypeName = "Sample.Failed", Payload = [],
        Status = JobStatus.Failed, Attempts = 1, MaxAttempts = 3, ScheduledAt = Scheduled,
        StartedAt = Scheduled.AddSeconds(1), NextRunAt = Scheduled.AddMinutes(5), Error = "timeout",
    };

    public static IReadOnlyList<JobEntry> All { get; } = [Succeeded, Pending, Running, DeadLetter, Failed];
}
