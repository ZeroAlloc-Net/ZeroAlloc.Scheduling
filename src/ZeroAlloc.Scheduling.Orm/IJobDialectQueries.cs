namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// The two statements whose SQL cannot be written portably.
/// </summary>
/// <remarks>
/// <para>
/// The claim bounds its result set, and the spelling differs: SQLite supports only
/// <c>LIMIT</c>, SQL Server only <c>OFFSET … FETCH NEXT</c>. PostgreSQL accepts
/// both. The recurring insert skips a duplicate live run, which SQLite and
/// PostgreSQL spell <c>ON CONFLICT DO NOTHING</c>. SQL Server has no such clause
/// and does not allow <c>IGNORE_DUP_KEY</c> on a filtered index, so it inserts
/// only where no live run exists, under an update range lock that makes the
/// check and the insert of concurrent seeds take turns. The ORM composes SQL at compile time from
/// the <c>[Command]</c> attribute, so one method cannot serve both and the pair is
/// implemented per family.
/// </para>
/// <para>
/// Everything else the store issues is plain ANSI and lives on the shared
/// <see cref="JobRepository"/>, including the claim read-back, which filters on
/// the claim token alone and needs no paging.
/// </para>
/// </remarks>
internal interface IJobDialectQueries
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due jobs by stamping
    /// <paramref name="claimToken"/> on them.
    /// </summary>
    /// <returns>The number of rows claimed.</returns>
    /// <remarks>
    /// Only rows still Pending or Failed are taken, so a poller that loses the
    /// race updates nothing. The status is checked again on the row being
    /// updated, not only in the subquery that picks the rows: under PostgreSQL's
    /// and SQL Server's default isolation, a poller that waited for another's lock
    /// would otherwise update the row it already claimed and run it a second time. The caller then reads back by token, which returns
    /// precisely the rows this statement claimed.
    /// </remarks>
    Task<int> ClaimPendingAsync(
        int runningStatus,
        int pendingStatus,
        int failedStatus,
        DateTimeOffset now,
        int batchSize,
        Guid claimToken,
        CancellationToken ct);

    /// <summary>
    /// Inserts a Pending run of a recurring job, unless the job type already has a
    /// live run: one that is Pending, Running or Failed. The unique index from
    /// migration 3 makes the check and the insert one atomic step, so instances that
    /// start at once seed a single run.
    /// </summary>
    /// <returns>1 when the run was inserted, 0 when a live run already existed.</returns>
    Task<int> InsertRecurringAsync(
        Guid id,
        string typeName,
        byte[] payload,
        int status,
        int maxAttempts,
        DateTimeOffset scheduledAt,
        string cronExpression,
        CancellationToken ct);
}
