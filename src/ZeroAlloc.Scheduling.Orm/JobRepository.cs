using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// The SQL the job store needs, as ZeroAlloc.ORM partial methods. The generator
/// emits parameter binding and materialisation at compile time, so nothing here
/// reflects at runtime.
/// </summary>
/// <remarks>
/// Plain ANSI throughout, so one repository serves every provider. The two
/// statements that cannot be written portably — both bound their result set —
/// live behind <see cref="IJobDialectQueries"/>, and the DDL lives in
/// <see cref="SchedulingOrmMigrations"/>.
/// </remarks>
internal sealed partial class JobRepository(IAsyncDbConnection connection)
{
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
    /// Reads back exactly the rows a claim stamped with <paramref name="claimToken"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The EF Core adapter originally selected candidate ids, updated those still
    /// claimable, then re-read the candidates that were now Running. That last
    /// step cannot tell <em>our</em> claim from a competing worker's: two pollers
    /// whose candidate sets overlap both read back rows the other claimed, so the
    /// same job is handed to both.
    /// </para>
    /// <para>
    /// A token stamped by the same UPDATE that takes the rows makes the read-back
    /// exact. Nothing else writes that token, so this returns the claimed set and
    /// nothing more, however many pollers ran concurrently.
    /// </para>
    /// <para>
    /// <c>UPDATE … RETURNING</c> would fold the two into one statement, but SQL
    /// Server has no equivalent for a multi-row update — <c>OUTPUT</c> writes to a
    /// table rather than the result set the ORM materialises — so the token is
    /// what makes the claim portable.
    /// </para>
    /// </remarks>
    [Query("""
        SELECT Id, TypeName, Payload, Status, Attempts, MaxAttempts, ScheduledAt, StartedAt, CompletedAt, NextRunAt, CronExpression, Error
        FROM SchedulingJobs
        WHERE ClaimToken = @claimToken
        ORDER BY ScheduledAt
        """)]
    public partial Task<IReadOnlyList<JobRow>> ReadClaimedAsync(Guid claimToken, CancellationToken ct);

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

    /// <summary>
    /// <see cref="MarkCompletedAsync"/>, applied only while the row still holds
    /// <paramref name="claimToken"/>.
    /// </summary>
    /// <remarks>
    /// A poll that takes back an expired claim stamps a new token, so a run that
    /// outlived its claim updates nothing here and cannot overwrite the run that
    /// took it back. The three outcome writes each have a claimed form like this.
    /// </remarks>
    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, CompletedAt = @completedAt
        WHERE Id = @id AND ClaimToken = @claimToken
        """)]
    public partial Task<int> MarkCompletedClaimedAsync(
        Guid id, Guid claimToken, int status, DateTimeOffset completedAt, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, Attempts = @attempts, ScheduledAt = @scheduledAt
        WHERE Id = @id
        """)]
    public partial Task<int> MarkForRetryAsync(
        Guid id, int status, int attempts, DateTimeOffset scheduledAt, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, Attempts = @attempts, ScheduledAt = @scheduledAt
        WHERE Id = @id AND ClaimToken = @claimToken
        """)]
    public partial Task<int> MarkForRetryClaimedAsync(
        Guid id, Guid claimToken, int status, int attempts, DateTimeOffset scheduledAt, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, Error = @error, CompletedAt = @completedAt
        WHERE Id = @id
        """)]
    public partial Task<int> DeadLetterAsync(
        Guid id, int status, string error, DateTimeOffset completedAt, CancellationToken ct);

    [Command("""
        UPDATE SchedulingJobs
        SET Status = @status, Error = @error, CompletedAt = @completedAt
        WHERE Id = @id AND ClaimToken = @claimToken
        """)]
    public partial Task<int> DeadLetterClaimedAsync(
        Guid id, Guid claimToken, int status, string error, DateTimeOffset completedAt, CancellationToken ct);

    /// <summary>
    /// Applies a recurring job's current definition to its Pending run. A changed
    /// schedule also takes the new due time; an unchanged one keeps the run's own,
    /// so a restart does not postpone a run that is due. A Running or Failed run
    /// is left alone: it is in the worker's hands.
    /// </summary>
    [Command("""
        UPDATE SchedulingJobs
        SET Payload = @payload,
            MaxAttempts = @maxAttempts,
            ScheduledAt = CASE WHEN CronExpression = @cronExpression THEN ScheduledAt ELSE @scheduledAt END,
            CronExpression = @cronExpression
        WHERE TypeName = @typeName AND CronExpression IS NOT NULL AND Status = @pendingStatus
        """)]
    public partial Task<int> UpdatePendingRecurringAsync(
        string typeName,
        byte[] payload,
        DateTimeOffset scheduledAt,
        string cronExpression,
        int maxAttempts,
        int pendingStatus,
        CancellationToken ct);
}
