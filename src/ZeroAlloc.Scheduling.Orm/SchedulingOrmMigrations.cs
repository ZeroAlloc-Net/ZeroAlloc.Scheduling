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
/// <para>
/// Version 3 allows one live run, Pending, Running or Failed, per recurring job
/// type, through a unique index over the live rows only, so the recurring seed is
/// one atomic insert. Releases before it could leave several live runs of one job,
/// so it first deletes all but the earliest. The status values in its SQL are those
/// of <see cref="JobStatus"/>.
/// </para>
/// <para>
/// Every provider's source is named <c>ZeroAlloc.Scheduling</c>, the name the
/// ORM records its versions under, so the schema shares a history table with
/// your own migrations without their version numbers colliding.
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
        """,
        """
        DELETE FROM SchedulingJobs
        WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3)
          AND EXISTS (
              SELECT 1 FROM SchedulingJobs AS Kept
              WHERE Kept.TypeName = SchedulingJobs.TypeName
                AND Kept.CronExpression IS NOT NULL AND Kept.Status IN (0, 1, 3)
                AND (Kept.ScheduledAt < SchedulingJobs.ScheduledAt
                     OR (Kept.ScheduledAt = SchedulingJobs.ScheduledAt AND Kept.Id < SchedulingJobs.Id)));
        CREATE UNIQUE INDEX IF NOT EXISTS UX_SchedulingJobs_LiveRecurring
            ON SchedulingJobs (TypeName)
            WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3);
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
        """,
        """
        DELETE FROM SchedulingJobs
        WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3)
          AND EXISTS (
              SELECT 1 FROM SchedulingJobs AS Kept
              WHERE Kept.TypeName = SchedulingJobs.TypeName
                AND Kept.CronExpression IS NOT NULL AND Kept.Status IN (0, 1, 3)
                AND (Kept.ScheduledAt < SchedulingJobs.ScheduledAt
                     OR (Kept.ScheduledAt = SchedulingJobs.ScheduledAt AND Kept.Id < SchedulingJobs.Id)));
        CREATE UNIQUE INDEX IF NOT EXISTS UX_SchedulingJobs_LiveRecurring
            ON SchedulingJobs (TypeName)
            WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3);
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
        """,
        """
        DELETE FROM SchedulingJobs
        WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3)
          AND EXISTS (
              SELECT 1 FROM SchedulingJobs AS Kept
              WHERE Kept.TypeName = SchedulingJobs.TypeName
                AND Kept.CronExpression IS NOT NULL AND Kept.Status IN (0, 1, 3)
                AND (Kept.ScheduledAt < SchedulingJobs.ScheduledAt
                     OR (Kept.ScheduledAt = SchedulingJobs.ScheduledAt AND Kept.Id < SchedulingJobs.Id)));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SchedulingJobs_LiveRecurring'
                       AND object_id = OBJECT_ID(N'SchedulingJobs'))
        CREATE UNIQUE INDEX UX_SchedulingJobs_LiveRecurring
            ON SchedulingJobs (TypeName)
            WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3);
        """);

    private sealed class Source(string createSql, string claimTokenSql, string liveRecurringSql) : IMigrationSource
    {
        // The ORM scopes recorded versions by this name, so it is persisted in every
        // database the schema is applied to. A fixed string rather than the default,
        // the type name, so renaming or moving this private type cannot make the
        // runner see the schema as new and apply version 1 again. One name for all
        // three providers, since a database only ever runs one of them.
        public string Name => "ZeroAlloc.Scheduling";

        private readonly IReadOnlyList<Migration> _migrations =
        [
            new Migration(1, "create_scheduling_jobs", createSql),
            new Migration(2, "add_claim_token", claimTokenSql),
            new Migration(3, "one_live_run_per_recurring_job", liveRecurringSql),
        ];

        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }
}
