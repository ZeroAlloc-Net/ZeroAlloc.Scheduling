using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm;

/// <summary>
/// The job schema, as an <see cref="IMigrationSource"/> you hand to
/// ZeroAlloc.ORM's <c>MigrationRunner</c> along with the dialect for your
/// database.
/// </summary>
/// <remarks>
/// <para>
/// Supplied as code rather than embedded <c>.sql</c> resources because the DDL
/// is the one genuinely provider-specific part — <c>BLOB</c> versus
/// <c>BYTEA</c>, <c>TEXT</c> versus <c>VARCHAR</c>. The resource-naming
/// convention gives a single migration set per assembly, which cannot express
/// that; selecting the statement by dialect can.
/// </para>
/// <para>
/// The index on <c>(Status, ScheduledAt)</c> matches the claim query's inner
/// SELECT exactly. Without it every poll scans a table that grows without
/// bound, since completed jobs are kept rather than deleted.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var runner = new MigrationRunner(connection, SchedulingOrmMigrations.Sqlite, new SqliteMigrationDialect());
/// await runner.RunAsync(ct);
/// </code>
/// </example>
public static class SchedulingOrmMigrations
{
    /// <summary>Schema for SQLite. Requires 3.35 or newer for RETURNING.</summary>
    public static IMigrationSource Sqlite { get; } = new Source("""
        CREATE TABLE IF NOT EXISTS SchedulingJobs (
            Id             BLOB NOT NULL PRIMARY KEY,
            TypeName       TEXT NOT NULL,
            Payload        BLOB NOT NULL,
            Status         INTEGER NOT NULL,
            Attempts       INTEGER NOT NULL,
            MaxAttempts    INTEGER NOT NULL,
            ScheduledAt    TEXT NOT NULL,
            StartedAt      TEXT NULL,
            CompletedAt    TEXT NULL,
            NextRunAt      TEXT NULL,
            CronExpression TEXT NULL,
            Error          TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_SchedulingJobs_Status_ScheduledAt
            ON SchedulingJobs (Status, ScheduledAt);
        """);

    /// <summary>Schema for PostgreSQL.</summary>
    public static IMigrationSource Postgres { get; } = new Source("""
        CREATE TABLE IF NOT EXISTS SchedulingJobs (
            Id             UUID NOT NULL PRIMARY KEY,
            TypeName       VARCHAR(512) NOT NULL,
            Payload        BYTEA NOT NULL,
            Status         INTEGER NOT NULL,
            Attempts       INTEGER NOT NULL,
            MaxAttempts    INTEGER NOT NULL,
            ScheduledAt    TIMESTAMPTZ NOT NULL,
            StartedAt      TIMESTAMPTZ NULL,
            CompletedAt    TIMESTAMPTZ NULL,
            NextRunAt      TIMESTAMPTZ NULL,
            CronExpression TEXT NULL,
            Error          TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_SchedulingJobs_Status_ScheduledAt
            ON SchedulingJobs (Status, ScheduledAt);
        """);

    private sealed class Source(string sql) : IMigrationSource
    {
        private readonly IReadOnlyList<Migration> _migrations =
            [new Migration(1, "create_scheduling_jobs", sql)];

        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }
}
