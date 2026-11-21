using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// <see cref="IJobDialectQueries"/> using <c>OFFSET … FETCH NEXT</c> — SQL Server.
/// </summary>
/// <remarks>
/// <c>OFFSET</c> is mandatory before <c>FETCH NEXT</c> in standard SQL, and the
/// clause requires an <c>ORDER BY</c>, so both appear even where the ordering is
/// incidental.
/// </remarks>
internal sealed partial class FetchFirstJobQueries(IAsyncDbConnection connection) : IJobDialectQueries
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
            OFFSET 0 ROWS FETCH NEXT @batchSize ROWS ONLY
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
        SELECT @id, @typeName, @payload, @status, 0, @maxAttempts, @scheduledAt, @cronExpression
        WHERE NOT EXISTS (
            SELECT 1 FROM SchedulingJobs WITH (UPDLOCK, SERIALIZABLE)
            WHERE TypeName = @typeName AND CronExpression IS NOT NULL AND Status IN (0, 1, 3))
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
