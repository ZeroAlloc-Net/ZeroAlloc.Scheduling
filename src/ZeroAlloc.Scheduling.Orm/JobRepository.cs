using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// The SQL the job store needs, as ZeroAlloc.ORM partial methods. The generator
/// emits parameter binding and materialisation at compile time, so nothing here
/// reflects at runtime.
/// </summary>
/// <remarks>
/// Plain ANSI throughout apart from <c>RETURNING</c>, which SQLite has supported
/// since 3.35 and PostgreSQL far longer, so one repository serves both. Only the
/// DDL differs, and that lives in <see cref="SchedulingOrmMigrations"/>.
/// </remarks>
internal sealed partial class JobRepository(IAsyncDbConnection connection)
{
    private const string JobColumns =
        "Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, " +
        "StartedAt, CompletedAt, NextRunAt, CronExpression, Error";

    [Command("""
        INSERT INTO SchedulingJobs
            (Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, CronExpression)
        VALUES
            (@id, @typeName, @payload, @status, 0, @maxAttempts, @scheduledAt, @cronExpression)
        """)]
    public partial Task<int> InsertAsync(
        Guid id,
        string typeName,
        byte[] payload,
        int status,
        int maxAttempts,
        DateTimeOffset scheduledAt,
        string? cronExpression,
        CancellationToken ct);

    /// <summary>
    /// Claims a batch and returns exactly the rows this statement claimed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One statement, not three. The EF Core adapter selects candidate ids,
    /// updates those still claimable, then re-reads the candidates that are now
    /// Running. That last step cannot tell <em>our</em> claim from a competing
    /// worker's: two pollers whose candidate sets overlap both read back rows
    /// the other claimed, so the same job can be handed to both.
    /// </para>
    /// <para>
    /// <c>RETURNING</c> yields precisely the rows this UPDATE changed, so the
    /// claim and the read-back cannot disagree and there is no window between
    /// them. The inner SELECT keeps the ordering and batch limit.
    /// </para>
    /// </remarks>
    [Query("""
        UPDATE SchedulingJobs
        SET Status = @runningStatus, StartedAt = @now
        WHERE Id IN (
            SELECT Id FROM SchedulingJobs
            WHERE (Status = @pendingStatus OR Status = @failedStatus)
              AND ScheduledAt <= @now
            ORDER BY ScheduledAt
            LIMIT @batchSize
        )
        RETURNING Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, StartedAt, CompletedAt, NextRunAt, CronExpression, Error
        """)]
    public partial Task<IReadOnlyList<JobRow>> ClaimPendingAsync(
        int runningStatus,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int batchSize,
        CancellationToken ct);

    [Query("""
        SELECT Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, StartedAt, CompletedAt, NextRunAt, CronExpression, Error
        FROM SchedulingJobs WHERE Id = @id
        """)]
    public partial Task<JobRow?> GetAsync(Guid id, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, CompletedAt = @completedAt
        WHERE Id = @id
        """)]
    public partial Task<int> MarkCompletedAsync(
        Guid id, int status, DateTimeOffset completedAt, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, Attempts = @attempts, ScheduledAt = @scheduledAt
        WHERE Id = @id
        """)]
    public partial Task<int> MarkForRetryAsync(
        Guid id, int status, int attempts, DateTimeOffset scheduledAt, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, Error = @error, CompletedAt = @completedAt
        WHERE Id = @id
        """)]
    public partial Task<int> DeadLetterAsync(
        Guid id, int status, string error, DateTimeOffset completedAt, CancellationToken ct);

    /// <summary>Finds an existing recurring definition by its type name.</summary>
    [Query("""
        SELECT Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, StartedAt, CompletedAt, NextRunAt, CronExpression, Error
        FROM SchedulingJobs
        WHERE TypeName = @typeName AND CronExpression IS NOT NULL
        ORDER BY ScheduledAt
        LIMIT 1
        """)]
    public partial Task<JobRow?> FindRecurringAsync(string typeName, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Payload = @payload, ScheduledAt = @scheduledAt, CronExpression = @cronExpression,
            MaxAttempts = @maxAttempts, Status = @status, Attempts = 0, Error = NULL
        WHERE Id = @id
        """)]
    public partial Task<int> UpdateRecurringAsync(
        Guid id,
        byte[] payload,
        DateTimeOffset scheduledAt,
        string? cronExpression,
        int maxAttempts,
        int status,
        CancellationToken ct);
}
