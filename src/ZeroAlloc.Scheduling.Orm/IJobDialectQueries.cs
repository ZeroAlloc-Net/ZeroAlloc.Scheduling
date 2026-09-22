namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// The two queries whose SQL cannot be written portably.
/// </summary>
/// <remarks>
/// <para>
/// Both bound their result set, and the spelling differs: SQLite supports only
/// <c>LIMIT</c>, SQL Server only <c>OFFSET … FETCH NEXT</c>. PostgreSQL accepts
/// both. The ORM composes SQL at compile time from the <c>[Query]</c> attribute,
/// so one method cannot serve both and the pair is implemented per family.
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
    /// race updates nothing. The caller then reads back by token, which returns
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

    /// <summary>Finds the existing recurring definition for a job type, if any.</summary>
    Task<JobRow?> FindRecurringAsync(string typeName, CancellationToken ct);
}
