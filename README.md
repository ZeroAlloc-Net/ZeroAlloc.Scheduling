# ZeroAlloc.Scheduling

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Scheduling.svg)](https://www.nuget.org/packages/ZeroAlloc.Scheduling)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated background jobs for .NET: mark a class with `[Job]` and get one-off, delayed and cron jobs with retries, durable stores and a dashboard, with no runtime registration cost and NativeAOT support.

## Why ZeroAlloc.Scheduling

- **No runtime registration cost.** `[Job]` registration is generated at compile time. Coravel's `Schedule()` allocates 696 B per call, measured in the same run.
- **Dispatch costs what a hand-written timer costs.** BenchmarkDotNet cannot tell a ZA.Scheduling dispatch from an empty method, just as it can't for a direct call. A test asserts the worker allocates nothing per job beyond the `JobContext` it hands to the job.
- **Works under NativeAOT and trimming.** Executors and DI registration are generated, with no reflection or assembly scanning. CI publishes and runs an AOT app over the in-memory, ORM and Redis stores and the Mediator, Resilience and Telemetry bridges.
- **Compile-time diagnostics.** The generator flags jobs it cannot register, such as nested or generic job types or two jobs that would get the same registration method.

## Install

```bash
dotnet add package ZeroAlloc.Scheduling
dotnet add package ZeroAlloc.Scheduling.InMemory
```

The source generator ships inside `ZeroAlloc.Scheduling`. Swap the in-memory store for EF Core, ZeroAlloc.ORM or Redis in production.

## Quick start

```csharp
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.InMemory;
using ZeroAlloc.Serialisation;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerializerDispatcher(); // generated, AOT-safe job serializer
builder.Services.AddScheduling(o => o.PollingInterval = TimeSpan.FromMilliseconds(100))
    .WithInMemoryStore()          // or EfCore, Orm or Redis in production
    .AddSendWelcomeEmailJob();    // generated for the [Job] class
using var host = builder.Build();
await host.StartAsync();

var scheduler = host.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync(new SendWelcomeEmailJob { To = "alice@example.com" });
await Task.Delay(500); // the worker picks it up, with retries if it throws
await host.StopAsync();

[Job(MaxAttempts = 5)]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class SendWelcomeEmailJob : IJob
{
    public required string To { get; init; }

    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        Console.WriteLine($"Welcome mail to {To}, attempt {ctx.Attempt}");
        return ValueTask.CompletedTask;
    }
}

[JsonSerializable(typeof(SendWelcomeEmailJob))]
internal sealed partial class AppJson : JsonSerializerContext;
```

For a recurring job, use `[Job(Cron = "0 * * * *")]` or `[Job(Every = Every.Hour)]`: the generator also emits a hosted service that schedules it at startup. The [getting started guide](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/getting-started.md) covers delays, retries and stores.

## What you get

- One-off jobs, delayed jobs and recurring jobs from cron expressions or fixed intervals.
- Exponential-backoff retry per job or globally, then dead-lettering.
- A background worker that polls the store in batches and runs each job through its generated executor.
- Targets .NET 8, 9 and 10.

Stores and integrations:

- [`ZeroAlloc.Scheduling.InMemory`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/stores.md#inmemory): in-process store for development and tests.
- [`ZeroAlloc.Scheduling.EfCore`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/stores.md#ef-core): EF Core store on SQL Server, PostgreSQL or SQLite.
- [`ZeroAlloc.Scheduling.Orm`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/stores.md#orm): ZeroAlloc.ORM store on SQLite, PostgreSQL or SQL Server, `WithOrm()`, net10.0 only.
- [`ZeroAlloc.Scheduling.Redis`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/stores.md#redis): Redis store for distributed deployments.
- [`ZeroAlloc.Scheduling.Mediator`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/mediator-bridge.md): run a job through ZeroAlloc.Mediator and its pipeline behaviors.
- [`ZeroAlloc.Scheduling.Resilience`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/resilience-bridge.md): retry, timeout and circuit-breaker around job execution.
- [`ZeroAlloc.Scheduling.Telemetry`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/telemetry-bridge.md): OpenTelemetry spans and metrics per job execution.
- [`ZeroAlloc.Scheduling.Dashboard`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/dashboard.md): web dashboard with job states, requeue and delete, via `app.MapJobsDashboard("/jobs")`. Put it behind authorization.
- [`ZeroAlloc.Scheduling.Dashboard.Blazor`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/dashboard.md): the dashboard as a `<JobsDashboard>` Blazor component.

![Scheduler dashboard](https://raw.githubusercontent.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/main/docs/screenshots/dashboard-desktop.png)

## Performance

Against Coravel 6.0.2 and a hand-rolled `BackgroundService + Timer`. BenchmarkDotNet 0.15.8, .NET 10.0.13, measured 2026-10-15. Allocations are exact; speed is a same-run ratio, because the benchmark host is a shared VM.

| Operation | Coravel | Hand-rolled Timer | ZA.Scheduling |
|---|---:|---:|---:|
| Register one job | 696 B per `Schedule()` call | — | **none at runtime, generated at compile time** |
| Register 100 jobs | 76,320 B, 15.36× the time of one call | — | **none at runtime** |
| Single dispatch | — | indistinguishable from an empty method | **indistinguishable from an empty method** |

What you pay for is `[Job]` itself: declarative retries, cron and store-backed persistence, not dispatch speed. Full methodology: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/performance.md).

## Learn more

- Documentation: [scheduling.zeroalloc.net](https://scheduling.zeroalloc.net)
- Reference: [source generator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/source-generator.md), [diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/diagnostics.md), [features and packages](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/index.md)
- Upgrading: [migrating to v2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/docs/migrating-to-v2.md)
- Related: [ZeroAlloc.Outbox](https://github.com/ZeroAlloc-Net/ZeroAlloc.Outbox), [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator), [ZeroAlloc.Resilience](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience), [ZeroAlloc.Telemetry](https://github.com/ZeroAlloc-Net/ZeroAlloc.Telemetry)
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues)
- License: MIT, see [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/blob/main/LICENSE)
