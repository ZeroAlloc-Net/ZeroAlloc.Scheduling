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
        SET Status = @runningStatus, StartedAt = @now, ClaimToken = @claimToken
        WHERE Id IN (
            SELECT Id FROM SchedulingJobs
            WHERE (Status = @pendingStatus OR Status = @failedStatus)
              AND ScheduledAt <= @now
            ORDER BY ScheduledAt
            OFFSET 0 ROWS FETCH NEXT @batchSize ROWS ONLY
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
        OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
        """)]
    public partial Task<JobRow?> FindRecurringAsync(string typeName, CancellationToken ct);
}
