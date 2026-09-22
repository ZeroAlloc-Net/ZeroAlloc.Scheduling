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
/// <c>BYTEA</c> versus <c>VARBINARY</c>, <c>TEXT</c> versus <c>VARCHAR</c>. The
/// resource-naming convention gives a single migration set per assembly, which
/// cannot express that; selecting the statement by dialect can.
/// </para>
/// <para>
/// The index on <c>(Status, ScheduledAt)</c> matches the claim query's inner
/// SELECT exactly. Without it every poll scans a table that grows without
/// bound, since completed jobs are kept rather than deleted.
/// </para>
/// <para>
/// Version numbers mean the same schema on every provider, so version 2 is
/// spelled three ways rather than folded into the SQL Server create — which
/// keeps a deployment's recorded version comparable across a provider move.
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
    /// <summary>Schema for SQLite.</summary>
    public static IMigrationSource Sqlite { get; } = new Source(
        """
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
        """,
        """
        ALTER TABLE SchedulingJobs ADD COLUMN ClaimToken BLOB NULL;
        CREATE INDEX IF NOT EXISTS IX_SchedulingJobs_ClaimToken
            ON SchedulingJobs (ClaimToken);
        """);

    /// <summary>Schema for PostgreSQL.</summary>
    public static IMigrationSource Postgres { get; } = new Source(
        """
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
        """,
        """
        ALTER TABLE SchedulingJobs ADD COLUMN IF NOT EXISTS ClaimToken UUID NULL;
        CREATE INDEX IF NOT EXISTS IX_SchedulingJobs_ClaimToken
            ON SchedulingJobs (ClaimToken);
        """);

    /// <summary>
    /// Schema for Microsoft SQL Server 2012 and newer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CREATE TABLE IF NOT EXISTS</c> and <c>CREATE INDEX IF NOT EXISTS</c>
    /// do not exist here, so both are guarded by catalogue lookups, which is the
    /// idiom that works across supported versions.
    /// </para>
    /// <para>
    /// <c>ADD COLUMN</c> is spelled <c>ADD</c> — the <c>COLUMN</c> keyword is a
    /// syntax error — and 2012 predates <c>OFFSET … FETCH</c>'s only alternative,
    /// which is why that is the floor rather than a later release.
    /// </para>
    /// </remarks>
    public static IMigrationSource SqlServer { get; } = new Source(
        """
        IF OBJECT_ID(N'SchedulingJobs', N'U') IS NULL
        CREATE TABLE SchedulingJobs (
            Id             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            TypeName       NVARCHAR(512) NOT NULL,
            Payload        VARBINARY(MAX) NOT NULL,
            Status         INT NOT NULL,
            Attempts       INT NOT NULL,
            MaxAttempts    INT NOT NULL,
            ScheduledAt    DATETIMEOFFSET NOT NULL,
            StartedAt      DATETIMEOFFSET NULL,
            CompletedAt    DATETIMEOFFSET NULL,
            NextRunAt      DATETIMEOFFSET NULL,
            CronExpression NVARCHAR(MAX) NULL,
            Error          NVARCHAR(MAX) NULL
        );
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SchedulingJobs_Status_ScheduledAt'
                       AND object_id = OBJECT_ID(N'SchedulingJobs'))
        CREATE INDEX IX_SchedulingJobs_Status_ScheduledAt
            ON SchedulingJobs (Status, ScheduledAt);
        """,
        """
        IF COL_LENGTH(N'SchedulingJobs', N'ClaimToken') IS NULL
        ALTER TABLE SchedulingJobs ADD ClaimToken UNIQUEIDENTIFIER NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SchedulingJobs_ClaimToken'
                       AND object_id = OBJECT_ID(N'SchedulingJobs'))
        CREATE INDEX IX_SchedulingJobs_ClaimToken ON SchedulingJobs (ClaimToken);
        """);

    private sealed class Source(string createSql, string claimTokenSql) : IMigrationSource
    {
        private readonly IReadOnlyList<Migration> _migrations =
        [
            new Migration(1, "create_scheduling_jobs", createSql),
            new Migration(2, "add_claim_token", claimTokenSql),
        ];

        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }
}
