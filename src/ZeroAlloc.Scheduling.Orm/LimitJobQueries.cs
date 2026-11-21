using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// <see cref="IJobDialectQueries"/> using <c>LIMIT</c> — SQLite and PostgreSQL.
/// </summary>
internal sealed partial class LimitJobQueries(IAsyncDbConnection connection) : IJobDialectQueries
{
    [Command("""
        UPDATE SchedulingJobs
        SET Attempts = CASE WHEN Status = @runningStatus THEN Attempts + 1 ELSE Attempts END,
            Status = @runningStatus, StartedAt = @now, ClaimToken = @claimToken
        WHERE Id IN (
            SELECT Id FROM SchedulingJobs
            WHERE ((Status = @pendingStatus OR Status = @failedStatus) AND ScheduledAt <= @now)
               OR (Status = @runningStatus AND StartedAt <= @staleBefore)
            ORDER BY ScheduledAt
            LIMIT @batchSize
        )
          AND ((Status = @pendingStatus OR Status = @failedStatus)
               OR (Status = @runningStatus AND StartedAt <= @staleBefore))
        """)]
    public partial Task<int> ClaimPendingAsync(
        int runningStatus,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        int batchSize,
        Guid claimToken,
        CancellationToken ct);

    [Command("""
        INSERT INTO SchedulingJobs
            (Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, CronExpression)
        VALUES
            (@id, @typeName, @payload, @status, 0, @maxAttempts, @scheduledAt, @cronExpression)
        ON CONFLICT DO NOTHING
        """)]
    public partial Task<int> InsertRecurringAsync(
        Guid id,
        string typeName,
        byte[] payload,
        int status,
        int maxAttempts,
        DateTimeOffset scheduledAt,
        string cronExpression,
        CancellationToken ct);
}
