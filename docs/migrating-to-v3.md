---
id: migrating-to-v3
title: Migrating to v3
slug: /docs/migrating-to-v3
description: Upgrade from ZeroAlloc.Scheduling 2.x to 3.0 — move to .NET 10 or .NET 11, and add the EF Core store's live-run index.
sidebar_position: 11
---

# Migrating to v3

ZeroAlloc.Scheduling 3.0 bundles two breaking changes:

1. Every package targets `net10.0` and `net11.0`. The `net8.0` and `net9.0` builds are gone.
2. The EF Core store allows one live run per recurring job, through a unique index that changes the `ScheduledJobs` schema.

## Target .NET 10 or .NET 11

.NET 8 and .NET 9 went out of support on 2026-11-10, and 3.0 no longer ships builds for them. An application on either has to move to .NET 10 or .NET 11 before it takes 3.0, or stay on 2.x. Nothing else about the API changes with the frameworks.

## EF Core: one live run per recurring job

A recurring job has one live run, one that is Pending, Running or Failed. Up to 2.x the EF Core store checked for that run and inserted a new one in two statements, so two callers that checked at the same moment both inserted it. The job then kept two live runs, and each scheduled its own next occurrence. This happened when instances started together and both seeded the job, and when the seed that repeats every `ClaimTimeout` raced the next occurrence the worker writes after a dead-letter.

3.0 adds a filtered unique index, `UX_ScheduledJobs_LiveRecurring`, on `TypeName` over the live runs of recurring jobs. The database now rejects a second live run, and the store skips that run instead of failing: the run that is already there is the one the job keeps. Finished runs, Succeeded or DeadLetter, and one-off jobs fall outside the index.

### What you need to do

A database created by 2.x may already hold more than one live run of a recurring job, and then the index cannot be created. Delete all but the earliest live run of each job first. The statement keeps the run with the earliest `ScheduledAt`, and the lowest `Id` among runs due at the same time, which is what the ORM store's migration 3 does:

```sql
-- SQL Server and SQLite
DELETE FROM ScheduledJobs
WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3)
  AND EXISTS (
      SELECT 1 FROM ScheduledJobs AS Kept
      WHERE Kept.TypeName = ScheduledJobs.TypeName
        AND Kept.CronExpression IS NOT NULL AND Kept.Status IN (0, 1, 3)
        AND (Kept.ScheduledAt < ScheduledJobs.ScheduledAt
             OR (Kept.ScheduledAt = ScheduledJobs.ScheduledAt AND Kept.Id < ScheduledJobs.Id)));
```

```sql
-- PostgreSQL: EF Core creates the names quoted, so they are case-sensitive
DELETE FROM "ScheduledJobs" AS j
WHERE j."CronExpression" IS NOT NULL AND j."Status" IN (0, 1, 3)
  AND EXISTS (
      SELECT 1 FROM "ScheduledJobs" AS kept
      WHERE kept."TypeName" = j."TypeName"
        AND kept."CronExpression" IS NOT NULL AND kept."Status" IN (0, 1, 3)
        AND (kept."ScheduledAt" < j."ScheduledAt"
             OR (kept."ScheduledAt" = j."ScheduledAt" AND kept."Id" < j."Id")));
```

The status values are those of `JobStatus`: 0 is Pending, 1 is Running and 3 is Failed.

Then add the index:

- **EF Core migrations.** Run `dotnet ef migrations add LiveRecurringRun --context SchedulingDbContext`. The generated migration creates the index. Put the `DELETE` above in front of it with `migrationBuilder.Sql(...)`, then run `dotnet ef database update`.
- **`EnsureCreated`.** It creates the schema only for a database that does not exist yet, so an existing database never gets the index. Run the `DELETE`, then create the index yourself:

```sql
-- SQL Server and SQLite
CREATE UNIQUE INDEX UX_ScheduledJobs_LiveRecurring ON ScheduledJobs (TypeName)
    WHERE CronExpression IS NOT NULL AND Status IN (0, 1, 3);
```

```sql
-- PostgreSQL
CREATE UNIQUE INDEX "UX_ScheduledJobs_LiveRecurring" ON "ScheduledJobs" ("TypeName")
    WHERE "CronExpression" IS NOT NULL AND "Status" IN (0, 1, 3);
```

Without the index, 3.0 behaves as 2.x did: the store still checks for a live run before it inserts, but two callers that check at once can both insert.
