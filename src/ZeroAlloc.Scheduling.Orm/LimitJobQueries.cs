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
        SET Status = @runningStatus, StartedAt = @now, ClaimToken = @claimToken
        WHERE Id IN (
            SELECT Id FROM SchedulingJobs
            WHERE (Status = @pendingStatus OR Status = @failedStatus)
              AND ScheduledAt <= @now
            ORDER BY ScheduledAt
            LIMIT @batchSize
        )
        """)]
    public partial Task<int> ClaimPendingAsync(
        int runningStatus,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int batchSize,
        Guid claimToken,
        CancellationToken ct);

    [Query("""
        SELECT Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, StartedAt, CompletedAt, NextRunAt, CronExpression, Error
        FROM SchedulingJobs
        WHERE TypeName = @typeName AND CronExpression IS NOT NULL
        ORDER BY ScheduledAt
        LIMIT 1
        """)]
    public partial Task<JobRow?> FindRecurringAsync(string typeName, CancellationToken ct);
}
