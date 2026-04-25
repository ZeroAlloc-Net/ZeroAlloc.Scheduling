---
id: docs-index
title: Documentation
slug: /docs
description: ZeroAlloc.Scheduling documentation index — navigate to all available pages.
sidebar_position: 0
---

# ZeroAlloc.Scheduling Documentation

Source-generated background job scheduler for .NET 8 and .NET 10.

## Reference

| # | Guide | Description |
|---|-------|-------------|
| 1 | [Getting Started](getting-started.md) | Install and schedule your first job in five minutes |
| 2 | [Source Generator](source-generator.md) | `[Job]`, `Every`, `Cron`, generated extension methods |
| 3 | [Backends](stores.md) | InMemory, EF Core, and Redis store configuration |
| 4 | [Dashboard](dashboard.md) | Embedded HTML dashboard and Blazor component |
| 5 | [Mediator Bridge](mediator-bridge.md) | Route job execution through ZeroAlloc.Mediator |
| 6 | [Resilience Bridge](resilience-bridge.md) | Wrap executors in retry, circuit-breaker, and timeout policies |
| 7 | [Diagnostics](diagnostics.md) | ZASCH001 compiler warning reference |
| 8 | [Performance](performance.md) | Throughput, allocation profile, and tuning guide |

## Quick Reference

```csharp
// Fire-and-forget job
[Job]
public sealed class SendWelcomeEmailJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) { ... }
}
services.AddScheduling().AddSchedulingInMemory().AddSendWelcomeEmailJob();
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
