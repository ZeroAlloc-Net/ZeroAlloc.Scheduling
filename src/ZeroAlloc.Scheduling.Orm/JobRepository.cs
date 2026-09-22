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
