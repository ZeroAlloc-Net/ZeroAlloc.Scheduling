namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// A job row as materialised by <see cref="JobRepository"/>.
/// </summary>
/// <remarks>
/// Mirrors the EF Core adapter's <c>JobEntryEntity</c> column for column, so the
/// two adapters read and write the same table and can be swapped without a data
/// migration. <c>Status</c> is stored as its integer value, which keeps the
/// column ordered and indexable on every provider.
/// </remarks>
internal sealed record JobRow(
    Guid Id,
    string TypeName,
    byte[] Payload,
    int Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? NextRunAt,
    string? CronExpression,
    string? Error);
