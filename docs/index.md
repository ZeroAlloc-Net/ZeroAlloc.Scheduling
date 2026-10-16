---
id: docs-index
title: Documentation
slug: /docs
description: ZeroAlloc.Scheduling documentation index — navigate to all available pages.
sidebar_position: 0
---

# ZeroAlloc.Scheduling Documentation

Source-generated background job scheduler for .NET 8, 9 and 10.

## Features

- **Source generator** — `[Job]` on a class emits a typed executor, DI extension method, and optional recurring startup (`IHostedService`)
- **Recurring jobs** — `[Job(Cron = "0 * * * *")]` or `[Job(Every = Every.Hour)]` — scheduled via Cronos at startup
- **Retry with backoff** — exponential retry up to `MaxAttempts` (per-job or global); dead-letters after exhaustion
- **Multiple backends** — InMemory (dev/test), EF Core (SQL Server / PostgreSQL / SQLite), ZeroAlloc.ORM, Redis
- **Dashboard** — embedded HTML/JS dashboard via `app.MapJobsDashboard("/jobs")`
- **Blazor component** — `<JobsDashboard>` Razor component for integration into Blazor apps
- **Mediator bridge** — `[Job]` + `IRequest<Unit>` auto-registers `MediatorJobTypeExecutor<T>`, routing execution through ZeroAlloc.Mediator pipeline behaviors
- **Native AOT compatible** — no reflection at runtime; all dispatch resolved at compile time

## Packages

| Package | Description |
|---------|-------------|
| `ZeroAlloc.Scheduling` | Core interfaces, worker service, scheduler, bundled source generator |
| `ZeroAlloc.Scheduling.Generator` | Source generator — **legacy**, already bundled inside `ZeroAlloc.Scheduling` |
| `ZeroAlloc.Scheduling.InMemory` | In-process store for development and testing |
| `ZeroAlloc.Scheduling.EfCore` | EF Core store (SQL Server, PostgreSQL, SQLite) |
| `ZeroAlloc.Scheduling.Orm` | ZeroAlloc.ORM store (SQLite, PostgreSQL, SQL Server), `WithOrm()`; net10.0 only |
| `ZeroAlloc.Scheduling.Redis` | Redis store for distributed deployments |
| `ZeroAlloc.Scheduling.Dashboard` | Embedded HTML dashboard (`MapJobsDashboard`) |
| `ZeroAlloc.Scheduling.Dashboard.Blazor` | Blazor component library |
| `ZeroAlloc.Scheduling.Mediator` | ZeroAlloc.Mediator bridge |
| `ZeroAlloc.Scheduling.Resilience` | ZeroAlloc.Resilience bridge: retry, timeout and circuit-breaker around job execution |
| `ZeroAlloc.Scheduling.Telemetry` | ZeroAlloc.Telemetry bridge: OpenTelemetry spans and metrics for job execution (`WithTelemetry()`) |

## Reference

| # | Guide | Description |
|---|-------|-------------|
| 1 | [Getting Started](getting-started.md) | Install and schedule your first job in five minutes |
| 2 | [Source Generator](source-generator.md) | `[Job]`, `Every`, `Cron`, generated extension methods |
| 3 | [Backends](stores.md) | InMemory, EF Core, and Redis store configuration |
| 4 | [Dashboard](dashboard.md) | Embedded HTML dashboard and Blazor component |
| 5 | [Mediator Bridge](mediator-bridge.md) | Route job execution through ZeroAlloc.Mediator |
| 6 | [Resilience Bridge](resilience-bridge.md) | Wrap executors in retry, circuit-breaker, and timeout policies |
| 7 | [Telemetry Bridge](telemetry-bridge.md) | Emit OpenTelemetry spans, counters, and histograms per job execution |
| 8 | [Diagnostics](diagnostics.md) | ZASCH001, ZASCH011, ZASCH012 and ZASCH013 compiler diagnostic reference |
| 9 | [Performance](performance.md) | Throughput, allocation profile, and tuning guide |
| 10 | [Migrating to v2](migrating-to-v2.md) | Choose a job serializer, adopt Outbox 4.0, replace the removed 1.x aliases |

## Quick Reference

```csharp
// Fire-and-forget job
[Job]
public sealed class SendWelcomeEmailJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) { ... }
}
services.AddSerializerDispatcher();   // AOT-safe serializer; see Getting Started
services.AddScheduling().WithInMemoryStore().AddSendWelcomeEmailJob();
await scheduler.EnqueueAsync(new SendWelcomeEmailJob { To = "user@example.com" }, ct);

// Recurring job (every hour)
[Job(Every = Every.Hour)]
public sealed class PurgeExpiredTokensJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) { ... }
}
// AddPurgeExpiredTokensJob() also registers an IHostedService that seeds the schedule on startup

// Recurring job (custom cron)
[Job(Cron = "0 9 * * 1-5")]   // weekdays at 09:00
public sealed class DailyReportJob : IJob { ... }

// Dashboard
app.MapJobsDashboard("/jobs");
```
