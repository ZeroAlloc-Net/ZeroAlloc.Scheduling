# Docs & CI Parity — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Bring ZeroAlloc.Scheduling to full parity with ZeroAlloc.Mediator for documentation, CI/CD workflows, repository configuration, and website integration.

**Architecture:** Mirror ZeroAlloc.Mediator's structure exactly — same Docusaurus frontmatter, same GitHub Actions versions, same release-please and renovate config shape, same PR template. Content is adapted for Scheduling's 8-package feature set. Website gets a new `apps/docs-scheduling` Docusaurus app at `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website`.

**Tech Stack:** GitHub Actions, Docusaurus 3.9.2, release-please, renovate, Markdown.

---

### Task 1: Fix CI workflow

**Files:**
- Modify: `.github/workflows/ci.yml`

**Step 1: Replace the entire file content**

```yaml
name: CI

on:
  push:
    branches: [main, 'release-please--**']
  pull_request:
    branches: [main]
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v6
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Restore tools
        run: dotnet tool restore

      - name: Run GitVersion
        id: gitversion
        run: |
          VERSION=$(dotnet gitversion /showvariable SemVer)
          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "Package version: $VERSION"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore -c Release -p:Version=${{ steps.gitversion.outputs.version }}

      - name: Test
        run: dotnet test --no-build -c Release --verbosity normal

      - name: Pack
        run: |
          for pkg in ZeroAlloc.Scheduling ZeroAlloc.Scheduling.Generator ZeroAlloc.Scheduling.EfCore \
                     ZeroAlloc.Scheduling.InMemory ZeroAlloc.Scheduling.Redis \
                     ZeroAlloc.Scheduling.Dashboard ZeroAlloc.Scheduling.Dashboard.Blazor \
                     ZeroAlloc.Scheduling.Mediator; do
            dotnet pack src/$pkg/$pkg.csproj --no-build -c Release \
              -p:PackageVersion=${{ steps.gitversion.outputs.version }} -o ./artifacts
          done

      - name: Push to NuGet (pre-release)
        if: github.event_name == 'push' && github.ref == 'refs/heads/main'
        run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

**Step 2: Commit**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Scheduling
git add .github/workflows/ci.yml
git commit -m "ci: upgrade to actions/checkout@v6, setup-dotnet@v5, add workflow_dispatch"
```

---

### Task 2: Fix release workflow

**Files:**
- Modify: `.github/workflows/release.yml`

**Step 1: Replace the entire file content**

```yaml
name: Release

on:
  release:
    types: [published]

jobs:
  publish:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v6
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Restore tools
        run: dotnet tool restore

      - name: Extract version from tag
        id: version
        run: |
          TAG="${{ github.event.release.tag_name }}"
          VERSION="${TAG#v}"
          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "Release version: $VERSION"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore -c Release -p:Version=${{ steps.version.outputs.version }}

      - name: Test
        run: dotnet test --no-build -c Release --verbosity normal

      - name: Pack
        run: |
          for pkg in ZeroAlloc.Scheduling ZeroAlloc.Scheduling.Generator ZeroAlloc.Scheduling.EfCore \
                     ZeroAlloc.Scheduling.InMemory ZeroAlloc.Scheduling.Redis \
                     ZeroAlloc.Scheduling.Dashboard ZeroAlloc.Scheduling.Dashboard.Blazor \
                     ZeroAlloc.Scheduling.Mediator; do
            dotnet pack src/$pkg/$pkg.csproj --no-build -c Release \
              -p:PackageVersion=${{ steps.version.outputs.version }} -o ./artifacts
          done

      - name: Push to NuGet
        run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate

      - name: Upload to GitHub Release
        run: gh release upload ${{ github.event.release.tag_name }} ./artifacts/*.nupkg
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: upgrade release workflow, add GitHub Release artifact upload"
```

---

### Task 3: Fix release-please config and trigger-website event type

**Files:**
- Modify: `release-please-config.json`
- Modify: `.github/workflows/trigger-website.yml`

**Step 1: Replace `release-please-config.json`**

```json
{
  "packages": {
    ".": {
      "release-type": "simple",
      "bump-minor-pre-major": true,
      "bump-patch-for-minor-pre-major": true,
      "changelog-sections": [
        { "type": "feat",     "section": "Features"         },
        { "type": "fix",      "section": "Bug Fixes"        },
        { "type": "perf",     "section": "Performance"      },
        { "type": "refactor", "section": "Code Refactoring" },
        { "type": "docs",     "section": "Documentation"    },
        { "type": "test",     "section": "Tests"            },
        { "type": "build",    "section": "Build System"     },
        { "type": "ci",       "section": "CI"               },
        { "type": "chore",    "section": "Chores"           },
        { "type": "revert",   "section": "Reverts"          }
      ]
    }
  },
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json"
}
```

**Step 2: Read `trigger-website.yml` and change `event-type: docs-updated` to `event-type: submodule-update`**

The file is at `.github/workflows/trigger-website.yml`. Find the line containing `event-type:` and change its value to `submodule-update`.

**Step 3: Commit**

```bash
git add release-please-config.json .github/workflows/trigger-website.yml
git commit -m "ci: fix release-please config structure, standardise website trigger event-type"
```

---

### Task 4: Update renovate.json and create PR template

**Files:**
- Modify: `renovate.json`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

**Step 1: Replace `renovate.json`**

```json
{
  "$schema": "https://docs.renovatebot.com/renovate-schema.json",
  "extends": [
    "config:recommended"
  ],
  "schedule": ["before 6am on monday"],
  "timezone": "Europe/Amsterdam",
  "labels": ["dependencies"],
  "packageRules": [
    {
      "description": "Ignore internal ZeroAlloc packages — managed by release-please",
      "matchPackagePrefixes": ["ZeroAlloc."],
      "enabled": false
    },
    {
      "description": "Group Roslyn analyzer packages",
      "matchPackageNames": [
        "Meziantou.Analyzer",
        "Roslynator.Analyzers",
        "ErrorProne.NET.CoreAnalyzers",
        "ErrorProne.NET.Structs",
        "NetFabric.Hyperlinq.Analyzer"
      ],
      "groupName": "Roslyn analyzers"
    },
    {
      "description": "Group xunit packages",
      "matchPackagePrefixes": ["xunit"],
      "groupName": "xunit"
    },
    {
      "description": "Group Microsoft.Extensions packages",
      "matchPackagePrefixes": ["Microsoft.Extensions."],
      "groupName": "Microsoft.Extensions"
    },
    {
      "description": "Group Microsoft.CodeAnalysis packages",
      "matchPackagePrefixes": ["Microsoft.CodeAnalysis."],
      "groupName": "Microsoft.CodeAnalysis"
    },
    {
      "description": "Group Microsoft.EntityFrameworkCore packages",
      "matchPackagePrefixes": ["Microsoft.EntityFrameworkCore."],
      "groupName": "Microsoft.EntityFrameworkCore"
    },
    {
      "description": "Group GitHub Actions",
      "matchManagers": ["github-actions"],
      "groupName": "GitHub Actions"
    },
    {
      "description": "Automerge patch updates",
      "matchUpdateTypes": ["patch"],
      "automerge": true
    }
  ]
}
```

**Step 2: Create `.github/PULL_REQUEST_TEMPLATE.md`**

```markdown
## Summary

<!-- Brief description of the changes and why they are needed -->

## Type of Change

- [ ] `feat` — New feature
- [ ] `fix` — Bug fix
- [ ] `perf` — Performance improvement
- [ ] `refactor` — Code refactoring (no behavior change)
- [ ] `docs` — Documentation only
- [ ] `test` — Adding or updating tests
- [ ] `build` / `ci` — Build system or CI changes
- [ ] `chore` — Maintenance

## Changes

-

## Breaking Changes

<!-- If this is a breaking change, describe what breaks and the migration path -->

None

## Test Plan

- [ ] All existing tests pass (`dotnet test`)
- [ ] New tests added for new functionality
- [ ] No allocation regression on job dispatch hot path

## Checklist

- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/)
- [ ] Code builds without warnings (`TreatWarningsAsErrors=true`)
- [ ] Generator code targets `netstandard2.0` where applicable
- [ ] No secrets or credentials in committed files
```

**Step 3: Commit**

```bash
git add renovate.json .github/PULL_REQUEST_TEMPLATE.md
git commit -m "ci: align renovate config with ecosystem standard, add PR template"
```

---

### Task 5: Create CHANGELOG.md

**Files:**
- Create: `CHANGELOG.md`

**Step 1: Create the file**

```markdown
# Changelog
```

That's it — release-please will populate this on first release.

**Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "chore: add CHANGELOG.md placeholder for release-please"
```

---

### Task 6: Create README.md

**Files:**
- Create: `README.md`

**Step 1: Create the file with the following content**

```markdown
# ZeroAlloc.Scheduling

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Scheduling.svg)](https://www.nuget.org/packages/ZeroAlloc.Scheduling)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

ZeroAlloc.Scheduling is a source-generated background job scheduler for .NET 8 and .NET 10. Decorate any class with `[Job]` and the source generator wires up the executor, DI registration, and recurring startup automatically — no reflection, no convention scanning at runtime.

## Install

```bash
dotnet add package ZeroAlloc.Scheduling
dotnet add package ZeroAlloc.Scheduling.InMemory   # or EfCore / Redis
```

The generator package must be added as an analyzer:

```xml
<PackageReference Include="ZeroAlloc.Scheduling.Generator" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Example

```csharp
// 1. Define a job — the generator picks it up automatically
[Job(Every = Every.Hour)]
public sealed class CleanupExpiredSessionsJob : IJob
{
    private readonly ISessionRepository _repo;
    public CleanupExpiredSessionsJob(ISessionRepository repo) => _repo = repo;

    public async ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
        => await _repo.DeleteExpiredAsync(ct);
}

// 2. Register — generated AddCleanupExpiredSessionsJob() wires executor + recurring startup
services.AddScheduling()
        .AddSchedulingInMemory()
        .AddCleanupExpiredSessionsJob();

// 3. Enqueue a one-off job from application code
public class OrderService(IScheduler scheduler)
{
    public async Task CompleteOrderAsync(Order order, CancellationToken ct)
    {
        await ProcessAsync(order, ct);
        await scheduler.EnqueueAsync(new SendOrderConfirmationJob(order.Id), ct);
    }
}
```

## Features

- **Source generator** — `[Job]` on a class emits a typed executor, DI extension method, and optional recurring startup (`IHostedService`)
- **Recurring jobs** — `[Job(Cron = "0 * * * *")]` or `[Job(Every = Every.Hour)]` — scheduled via Cronos at startup
- **Retry with backoff** — exponential retry up to `MaxAttempts` (per-job or global); dead-letters after exhaustion
- **Multiple backends** — InMemory (dev/test), EF Core (SQL Server / PostgreSQL / SQLite), Redis
- **Dashboard** — embedded HTML/JS dashboard via `app.MapJobsDashboard("/jobs")`
- **Blazor component** — `<JobsDashboard>` Razor component for integration into Blazor apps
- **Mediator bridge** — `[Job]` + `IRequest<Unit>` auto-registers `MediatorJobTypeExecutor<T>`, routing execution through ZeroAlloc.Mediator pipeline behaviors
- **Native AOT compatible** — no reflection at runtime; all dispatch resolved at compile time

## Packages

| Package | Description |
|---------|-------------|
| `ZeroAlloc.Scheduling` | Core interfaces, worker service, scheduler |
| `ZeroAlloc.Scheduling.Generator` | Source generator (analyzer reference) |
| `ZeroAlloc.Scheduling.InMemory` | In-process store for development and testing |
| `ZeroAlloc.Scheduling.EfCore` | EF Core store (SQL Server, PostgreSQL, SQLite) |
| `ZeroAlloc.Scheduling.Redis` | Redis store for distributed deployments |
| `ZeroAlloc.Scheduling.Dashboard` | Embedded HTML dashboard (`MapJobsDashboard`) |
| `ZeroAlloc.Scheduling.Dashboard.Blazor` | Blazor component library |
| `ZeroAlloc.Scheduling.Mediator` | ZeroAlloc.Mediator bridge |

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](docs/getting-started.md) | Install and schedule your first job in five minutes |
| [Source Generator](docs/source-generator.md) | `[Job]`, `Every`, `Cron`, generated extension methods |
| [Backends](docs/stores.md) | InMemory, EF Core, and Redis store configuration |
| [Dashboard](docs/dashboard.md) | Embedded HTML dashboard and Blazor component |
| [Mediator Bridge](docs/mediator-bridge.md) | Route job execution through ZeroAlloc.Mediator |
| [Diagnostics](docs/diagnostics.md) | ZASCH001 compiler warning reference |
| [Performance](docs/performance.md) | Throughput, allocation profile, and tuning guide |

## License

MIT
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add README.md"
```

---

### Task 7: Create docs/index.md

**Files:**
- Create: `docs/index.md`

**Step 1: Create the file**

```markdown
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
| 6 | [Diagnostics](diagnostics.md) | ZASCH001 compiler warning reference |
| 7 | [Performance](performance.md) | Throughput, allocation profile, and tuning guide |

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
```

**Step 2: Commit**

```bash
git add docs/index.md
git commit -m "docs: add docs/index.md"
```

---

### Task 8: Create docs/getting-started.md

**Files:**
- Create: `docs/getting-started.md`

**Step 1: Create the file**

```markdown
---
id: getting-started
title: Getting Started
slug: /
description: Install ZeroAlloc.Scheduling and schedule your first job in under five minutes.
sidebar_position: 1
---

# Getting Started

ZeroAlloc.Scheduling is a background job scheduler for .NET 8 and .NET 10. You decorate a class with `[Job]`, and the Roslyn source generator emits the executor, DI registration, and optional recurring startup for you at build time — no reflection, no convention scanning, no `IServiceCollection.Scan`.

## Installation

```bash
dotnet add package ZeroAlloc.Scheduling
dotnet add package ZeroAlloc.Scheduling.InMemory
```

The generator runs as an analyzer:

```xml
<PackageReference Include="ZeroAlloc.Scheduling.Generator" Version="*"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Your First Job

### Step 1 — Define the job

Implement `IJob` and decorate with `[Job]`.

```csharp
using ZeroAlloc.Scheduling;

[Job]
public sealed class SendWelcomeEmailJob : IJob
{
    public required string To { get; init; }

    public async ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        // ctx.JobId, ctx.Attempt, ctx.ScheduledAt are available
        Console.WriteLine($"Sending welcome email to {To} (attempt {ctx.Attempt})");
        await Task.Delay(100, ct); // simulate work
    }
}
```

### Step 2 — Register

The generator emits `AddSendWelcomeEmailJob()`. Call it alongside `AddScheduling` and your chosen backend.

```csharp
builder.Services
    .AddScheduling()
    .AddSchedulingInMemory()
    .AddSendWelcomeEmailJob();
```

### Step 3 — Enqueue

Inject `IScheduler` and enqueue.

```csharp
public class UserService(IScheduler scheduler)
{
    public async Task RegisterAsync(string email, CancellationToken ct)
    {
        // ... create user ...
        await scheduler.EnqueueAsync(new SendWelcomeEmailJob { To = email }, ct);
    }
}
```

### Step 4 — Build and run

```bash
dotnet run
```

The background worker polls the store every 5 seconds (configurable), claims pending jobs, and executes them. On failure it retries with exponential backoff up to `DefaultMaxAttempts` (default: 3), then dead-letters.

## Recurring Jobs

Add `Every` or `Cron` to the attribute. The generator also emits an `IHostedService` that seeds the schedule on startup.

```csharp
[Job(Every = Every.Hour)]
public sealed class PurgeExpiredSessionsJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) { ... }
}

// Or with a custom cron expression:
[Job(Cron = "0 9 * * 1-5")]   // weekdays at 09:00 UTC
public sealed class DailyDigestJob : IJob { ... }
```

Register the same way — `AddPurgeExpiredSessionsJob()` registers both the executor and the startup service.

## Dashboard

Serve the built-in HTML dashboard with one line:

```csharp
app.MapJobsDashboard("/jobs");
```

Open `/jobs/` in your browser to see pending, running, succeeded, failed, and dead-lettered jobs with live auto-refresh.

## Configuration

```csharp
builder.Services.AddScheduling(opt =>
{
    opt.PollingInterval   = TimeSpan.FromSeconds(5);  // default
    opt.BatchSize         = 20;                        // jobs per poll
    opt.RetryBaseDelay    = TimeSpan.FromSeconds(2);   // exponential base
    opt.DefaultMaxAttempts = 3;                        // global retry limit
});
```

## Next Steps

| Guide | What you'll learn |
|-------|-------------------|
| [Source Generator](source-generator.md) | All `[Job]` options, `Every` enum, generated code |
| [Backends](stores.md) | Switch from InMemory to EF Core or Redis |
| [Dashboard](dashboard.md) | Dashboard options, Blazor component |
| [Mediator Bridge](mediator-bridge.md) | Route jobs through ZeroAlloc.Mediator |
| [Performance](performance.md) | Tuning batch size, polling interval, concurrency |
```

**Step 2: Commit**

```bash
git add docs/getting-started.md
git commit -m "docs: add getting-started.md"
```

---

### Task 9: Create docs/source-generator.md

**Files:**
- Create: `docs/source-generator.md`

**Step 1: Create the file**

```markdown
---
id: source-generator
title: Source Generator
slug: /docs/source-generator
description: The [Job] attribute, Every enum, Cron expressions, MaxAttempts, and the code generated for each job type.
sidebar_position: 2
---

# Source Generator

ZeroAlloc.Scheduling ships a Roslyn incremental source generator (`ZeroAlloc.Scheduling.Generator`) that runs during `dotnet build`. For every class decorated with `[Job]`, it emits three things: an executor class, an optional recurring startup service, and a DI extension method.

## The `[Job]` Attribute

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class JobAttribute : Attribute
{
    public string?  Cron        { get; set; }   // cron expression (standard 5-field)
    public Every    Every       { get; set; }   // shorthand schedule
    public int      MaxAttempts { get; set; }   // 0 = use global default
}
```

A job type must implement `IJob`:

```csharp
public interface IJob
{
    ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct);
}
```

`JobContext` carries `JobId`, `Attempt`, `ScheduledAt`, and `Services` (the DI `IServiceProvider` for the current scope).

## Scheduling Options

### Fire-and-forget (no schedule)

```csharp
[Job]
public sealed class SendInvoiceJob : IJob { ... }

[Job(MaxAttempts = 5)]   // override global default for this type
public sealed class ProcessPaymentJob : IJob { ... }
```

Enqueue manually via `IScheduler.EnqueueAsync`.

### Recurring — `Every` shorthand

```csharp
public enum Every
{
    Minute, FiveMinutes, FifteenMinutes, ThirtyMinutes,
    Hour, SixHours, TwelveHours, Day, Week
}
```

```csharp
[Job(Every = Every.FifteenMinutes)]
public sealed class RefreshExchangeRatesJob : IJob { ... }
```

### Recurring — custom Cron expression

Standard 5-field cron (minute hour day-of-month month day-of-week):

```csharp
[Job(Cron = "0 */4 * * *")]    // every 4 hours
public sealed class BackupJob : IJob { ... }

[Job(Cron = "30 23 * * 0")]    // Sundays at 23:30
public sealed class WeeklyReportJob : IJob { ... }
```

Cron expressions are parsed by [Cronos](https://github.com/HangfireIO/Cronos).

## What Gets Generated

For a type `MyApp.SendInvoiceJob` decorated with `[Job(MaxAttempts = 5)]`, the generator emits (simplified):

```csharp
// <auto-generated/>
namespace MyApp;

// 1. Executor — implements IJobTypeExecutor, wired by SchedulingWorkerService
internal sealed class SendInvoiceJobJobTypeExecutor : IJobTypeExecutor
{
    private readonly IJobSerializer _serializer;
    public SendInvoiceJobJobTypeExecutor(IJobSerializer serializer) => _serializer = serializer;

    public string TypeName    => "MyApp.SendInvoiceJob";
    public int    MaxAttempts => 5;

    public async ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
    {
        var job = _serializer.Deserialize<global::MyApp.SendInvoiceJob>(payload);
        await job.ExecuteAsync(ctx, ct).ConfigureAwait(false);
    }
}

// 2. DI extension — call this in your AddXxx registration chain
public static partial class SchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddSendInvoiceJob(this IServiceCollection services)
    {
        services.AddTransient<IJobTypeExecutor, SendInvoiceJobJobTypeExecutor>();
        return services;
    }
}
```

For a recurring job (`[Job(Every = Every.Hour)]`), the generator additionally emits an `IHostedService` that calls `IJobStore.UpsertRecurringAsync` at startup to seed the first scheduled run.

## Mediator Bridge Detection

When a `[Job]` type also implements `ZeroAlloc.Mediator.IRequest<Unit>`, the generator automatically switches to a mediator-aware registration:

```csharp
// Instead of a direct executor class, the generated AddXxxJob() registers:
services.AddTransient<IJobTypeExecutor,
    global::ZeroAlloc.Scheduling.Mediator.MediatorJobTypeExecutor<global::MyApp.MyJob>>();
```

This routes execution through the ZeroAlloc.Mediator pipeline (behaviors, interceptors, etc.). See [Mediator Bridge](mediator-bridge.md) for details.

> **Diagnostic ZASCH001:** If you set `[Job(MaxAttempts = N)]` on a type that also implements `IRequest<Unit>`, the generator emits a warning — the `MaxAttempts` value is not honoured on the mediator path. See [Diagnostics](diagnostics.md).

## Reference Table

| Attribute argument | Effect | Generated artifact |
|--------------------|--------|--------------------|
| _(none)_ | Fire-and-forget | Executor + `AddXxxJob()` |
| `Every = Every.Hour` | Hourly recurring | Executor + startup `IHostedService` + `AddXxxJob()` |
| `Cron = "0 * * * *"` | Custom recurring | Executor + startup `IHostedService` + `AddXxxJob()` |
| `MaxAttempts = N` | Per-job retry limit | `MaxAttempts => N` property on executor |
```

**Step 2: Commit**

```bash
git add docs/source-generator.md
git commit -m "docs: add source-generator.md"
```

---

### Task 10: Create docs/stores.md

**Files:**
- Create: `docs/stores.md`

**Step 1: Create the file**

```markdown
---
id: stores
title: Backends
slug: /docs/stores
description: Configure the InMemory, EF Core, or Redis job store backends.
sidebar_position: 3
---

# Backends

ZeroAlloc.Scheduling separates the job execution engine from the persistence layer. The engine (`SchedulingWorkerService`) talks to `IJobStore`. Three store implementations are provided; pick one per application.

## InMemory

Best for development, testing, and single-instance applications that don't need job persistence across restarts.

```bash
dotnet add package ZeroAlloc.Scheduling.InMemory
```

```csharp
services.AddScheduling()
        .AddSchedulingInMemory()
        .AddMyJob();
```

Jobs are stored in a `ConcurrentDictionary` inside the process. All state is lost on restart.

## EF Core

Persist jobs to any database supported by EF Core (SQL Server, PostgreSQL, SQLite).

```bash
dotnet add package ZeroAlloc.Scheduling.EfCore
```

```csharp
services.AddScheduling()
        .AddSchedulingEfCore(opt =>
            opt.UseSqlite("Data Source=jobs.db"))
        .AddMyJob();
```

The store uses `SchedulingDbContext`. Apply the migration before first run:

```bash
dotnet ef migrations add InitScheduling --context SchedulingDbContext
dotnet ef database update --context SchedulingDbContext
```

`FetchPendingAsync` uses an atomic `ExecuteUpdateAsync` pattern (EF Core 7+) to claim jobs without loading them first, preventing double-dispatch under concurrent workers.

## Redis

For distributed deployments with multiple worker instances.

```bash
dotnet add package ZeroAlloc.Scheduling.Redis
```

```csharp
services.AddScheduling()
        .AddSchedulingRedis("localhost:6379")
        .AddMyJob();
```

Jobs are stored as Redis hashes. The store maintains three tracking Sets (`jobs:succeeded`, `jobs:failed`, `jobs:deadletter`) for O(1) summary queries without scanning all keys.

## Dashboard Integration

All three stores also implement `IJobDashboardStore`, which powers the dashboard API. EF Core and Redis register `IJobDashboardStore` automatically. InMemory implements it directly on `InMemoryJobStore`.

If you implement a custom store, register `IJobDashboardStore` explicitly:

```csharp
services.TryAddScoped<IJobDashboardStore>(sp =>
    (IJobDashboardStore)sp.GetRequiredService<IJobStore>());
```

## Choosing a Backend

| Requirement | Recommended backend |
|-------------|---------------------|
| Local development / unit tests | InMemory |
| Single-server with persistence | EF Core (SQLite or SQL Server) |
| Multi-server / distributed | Redis |
| Custom / existing database | Implement `IJobStore` + `IJobDashboardStore` |
```

**Step 2: Commit**

```bash
git add docs/stores.md
git commit -m "docs: add stores.md (InMemory, EF Core, Redis backends)"
```

---

### Task 11: Create docs/dashboard.md

**Files:**
- Create: `docs/dashboard.md`

**Step 1: Create the file**

```markdown
---
id: dashboard
title: Dashboard
slug: /docs/dashboard
description: Embedded HTML dashboard and Blazor component for monitoring and managing background jobs.
sidebar_position: 4
---

# Dashboard

ZeroAlloc.Scheduling ships two dashboard options: a self-contained embedded HTML dashboard served via Minimal API, and a Blazor component for integration into existing Blazor applications.

## Embedded HTML Dashboard

One line wires up the full dashboard:

```bash
dotnet add package ZeroAlloc.Scheduling.Dashboard
```

```csharp
app.MapJobsDashboard("/jobs");
```

Open `/jobs/` in a browser to see:

- **Summary cards** — counts for pending, running, succeeded, failed, and dead-lettered jobs
- **Jobs table** — ID, type, status, attempts, scheduled time, last error, and actions
- **Auto-refresh** — the page polls all API endpoints every 5 seconds
- **Requeue** — re-enqueue a dead-lettered job with one click
- **Delete** — remove any job from the store

The dashboard is served from an embedded resource (no static file middleware required). It calls the following API endpoints automatically:

| Endpoint | Description |
|----------|-------------|
| `GET /jobs/api/summary` | Job counts by status |
| `GET /jobs/api/pending` | Pending jobs (page 1, 50 per page) |
| `GET /jobs/api/running` | Currently running jobs |
| `GET /jobs/api/failed` | Failed jobs awaiting retry |
| `GET /jobs/api/succeeded` | Recently succeeded jobs |
| `POST /jobs/api/{id}/requeue` | Re-enqueue a dead-lettered job |
| `DELETE /jobs/api/{id}` | Delete a job |

### Authentication

`MapJobsDashboard` returns an `IEndpointConventionBuilder`, so standard ASP.NET Core authorization policies apply:

```csharp
app.MapJobsDashboard("/jobs").RequireAuthorization("AdminOnly");
```

## Blazor Component

For Blazor Server or WASM applications that want to embed the dashboard inside their own layout:

```bash
dotnet add package ZeroAlloc.Scheduling.Dashboard.Blazor
```

```csharp
// Register the HTTP client pointing at your dashboard API base URL
services.AddJobsDashboardBlazor(new Uri("https://myapp.example.com/jobs/"));
```

```razor
@* In any .razor page or layout *@
@using ZeroAlloc.Scheduling.Dashboard.Blazor

<JobsDashboard />
```

The component uses `JobsDashboardClient` internally, which calls the same API endpoints as the HTML dashboard. State refreshes automatically every 5 seconds via `System.Threading.Timer`.
```

**Step 2: Commit**

```bash
git add docs/dashboard.md
git commit -m "docs: add dashboard.md"
```

---

### Task 12: Create docs/mediator-bridge.md

**Files:**
- Create: `docs/mediator-bridge.md`

**Step 1: Create the file**

```markdown
---
id: mediator-bridge
title: Mediator Bridge
slug: /docs/mediator-bridge
description: Route job execution through ZeroAlloc.Mediator pipeline behaviors using IRequest<Unit>.
sidebar_position: 5
---

# Mediator Bridge

The `ZeroAlloc.Scheduling.Mediator` package lets you implement a job as a ZeroAlloc.Mediator request. Job execution is routed through the mediator, so all pipeline behaviors (logging, validation, tracing, transactions) apply automatically.

## Installation

```bash
dotnet add package ZeroAlloc.Scheduling.Mediator
```

## Usage

Implement both `IJob` and `IRequest<Unit>` on the same type:

```csharp
using ZeroAlloc.Scheduling;
using ZeroAlloc.Mediator;

[Job(Every = Every.Hour)]
public sealed class GenerateInvoicesJob : IJob, IRequest<Unit>
{
    // IJob.ExecuteAsync is NOT called — the handler below is used instead
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => default;
}
```

Implement the handler:

```csharp
public sealed class GenerateInvoicesHandler : IRequestHandler<GenerateInvoicesJob, Unit>
{
    private readonly IInvoiceService _invoices;
    public GenerateInvoicesHandler(IInvoiceService invoices) => _invoices = invoices;

    public async ValueTask<Unit> Handle(GenerateInvoicesJob request, CancellationToken ct)
    {
        await _invoices.GeneratePendingAsync(ct);
        return Unit.Value;
    }
}
```

Register — the generator detects `IRequest<Unit>` and emits a mediator-aware `AddGenerateInvoicesJob()`:

```csharp
services.AddScheduling()
        .AddSchedulingInMemory()
        .AddSchedulingMediator()      // no-op, retained for source compatibility
        .AddGenerateInvoicesJob();    // registers MediatorJobTypeExecutor<T>
```

ZeroAlloc.Mediator's own generator registers `GenerateInvoicesHandler` automatically. The job now flows through the full mediator pipeline on every execution.

## How It Works

The source generator detects when a `[Job]` type implements `ZeroAlloc.Mediator.IRequest<ZeroAlloc.Mediator.Unit>`. Instead of emitting a direct executor class, it emits:

```csharp
services.AddTransient<IJobTypeExecutor,
    MediatorJobTypeExecutor<GenerateInvoicesJob>>();
```

`MediatorJobTypeExecutor<T>` deserialises the payload, then calls `IRequestHandler<T, Unit>.Handle` — routing the call through the mediator pipeline.

## Pipeline Behaviors

Because execution goes through the mediator, all registered pipeline behaviors apply:

```csharp
// Logging behavior — wraps every job with structured log entries
[PipelineBehavior(Order = 0)]
public static class JobLoggingBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
    {
        using var _ = logger.BeginScope(new { JobType = typeof(TRequest).Name });
        logger.LogInformation("Job started");
        var result = await next(request, ct);
        logger.LogInformation("Job completed");
        return result;
    }
}
```

## Limitations

- **`MaxAttempts` is not honoured on the mediator path.** The `MediatorJobTypeExecutor<T>` always returns `MaxAttempts = 0` (use global default). If you set `[Job(MaxAttempts = N)]` on a mediator bridge type, the generator emits diagnostic [ZASCH001](diagnostics.md). Use `SchedulingOptions.DefaultMaxAttempts` to control retry count for mediator jobs.
- **`IJob.ExecuteAsync` is never called.** The direct execution path is bypassed entirely; only the handler is invoked.
```

**Step 2: Commit**

```bash
git add docs/mediator-bridge.md
git commit -m "docs: add mediator-bridge.md"
```

---

### Task 13: Create docs/diagnostics.md

**Files:**
- Create: `docs/diagnostics.md`

**Step 1: Create the file**

```markdown
---
id: diagnostics
title: Diagnostics
slug: /docs/diagnostics
description: ZASCH001 compiler diagnostic reference — causes, examples, and fixes.
sidebar_position: 6
---

# Diagnostics

The ZeroAlloc.Scheduling source generator emits compiler diagnostics to catch misconfigurations at build time.

## ZASCH001 — MaxAttempts ignored for mediator bridge job

**Severity:** Warning (build succeeds)

**Message:**
```
Job type 'T' specifies MaxAttempts=N but implements IRequest<Unit> — MaxAttempts is not
honoured for mediator bridge jobs. Remove [Job(MaxAttempts=...)] or use manual registration.
```

### Cause

`[Job(MaxAttempts = N)]` is set on a type that also implements `IRequest<Unit>`. The generator routes these types through `MediatorJobTypeExecutor<T>`, which always returns `MaxAttempts = 0` (global default). The `MaxAttempts` value from the attribute is ignored.

### Example that triggers ZASCH001

```csharp
[Job(MaxAttempts = 5)]            // ← ZASCH001 — MaxAttempts is silently discarded
public sealed class SendReportJob : IJob, IRequest<Unit>
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => default;
}
```

### Fix option A — Remove MaxAttempts, use the global default

```csharp
[Job]   // no MaxAttempts — uses SchedulingOptions.DefaultMaxAttempts
public sealed class SendReportJob : IJob, IRequest<Unit> { ... }
```

Configure the global default in startup:

```csharp
services.AddScheduling(opt => opt.DefaultMaxAttempts = 5);
```

### Fix option B — Use manual registration to bypass the generator

If you need per-job `MaxAttempts` on a mediator type, skip the generator and register manually:

```csharp
[Job(MaxAttempts = 5)]
public sealed class SendReportJob : IJob, IRequest<Unit> { ... }

// In startup — do NOT call AddSendReportJob() (that would trigger the generator path)
services.AddTransient<IJobTypeExecutor, MediatorJobTypeExecutor<SendReportJob>>();
// Note: MediatorJobTypeExecutor.MaxAttempts => 0 regardless; this fix requires a custom executor.
```

For full per-job MaxAttempts control on the mediator path, implement a custom `IJobTypeExecutor` subclass that sets the desired value.

### Suppress (not recommended)

```csharp
#pragma warning disable ZASCH001
[Job(MaxAttempts = 5)]
public sealed class SendReportJob : IJob, IRequest<Unit> { ... }
#pragma warning restore ZASCH001
```
```

**Step 2: Commit**

```bash
git add docs/diagnostics.md
git commit -m "docs: add diagnostics.md (ZASCH001 reference)"
```

---

### Task 14: Create docs/performance.md

**Files:**
- Create: `docs/performance.md`

**Step 1: Create the file**

```markdown
---
id: performance
title: Performance
slug: /docs/performance
description: Throughput characteristics, allocation profile, and tuning guide for ZeroAlloc.Scheduling.
sidebar_position: 7
---

# Performance

ZeroAlloc.Scheduling is designed for moderate-to-high-throughput background job workloads where scheduler overhead should be negligible relative to the job work itself. This page covers the allocation profile of the scheduler, key tuning parameters, and guidance on when the design choices matter in practice.

## Allocation Profile

The hot path — polling the store, claiming jobs, deserialising payloads, executing handlers — performs the following allocations per job:

| Step | Allocation |
|------|-----------|
| `FetchPendingAsync` SQL/Redis query | Network buffer (store-dependent) |
| Payload deserialisation (`DefaultJobSerializer`) | Boxed job object (JSON reflection) |
| `IServiceScope` creation per job | 1 managed object |
| Executor dispatch | 0 (compile-time static call via generated class) |
| `ValueTask` returned by `ExecuteAsync` | 0 if synchronous completion |

The executor dispatch itself is zero-allocation — the generator emits a concrete class with a direct `_serializer.Deserialize<T>` call. There is no dictionary lookup, no `Type.GetType`, no virtual dispatch on the executor.

The serialiser (`DefaultJobSerializer`) uses `System.Text.Json` with reflection-based serialisation by default. For AOT scenarios, register a source-generated `JsonSerializerContext`.

## Tuning Parameters

### `PollingInterval`

How often the worker wakes to claim pending jobs. Default: 5 seconds.

```csharp
services.AddScheduling(opt => opt.PollingInterval = TimeSpan.FromSeconds(2));
```

Lower values reduce job start latency at the cost of more store queries when the queue is empty. For fire-and-forget jobs that must start quickly, consider lowering to 1–2 seconds.

### `BatchSize`

Maximum jobs claimed per poll cycle. Default: 20.

```csharp
services.AddScheduling(opt => opt.BatchSize = 50);
```

Increase for higher throughput at the cost of larger transactions. Each claimed job acquires a lock in the store (an atomic UPDATE for EF Core, a transaction for Redis).

### `RetryBaseDelay`

Base delay for exponential backoff between retries. Default: 2 seconds.

Retry delays follow `base * 2^(attempt-1)`:

| Attempt | Delay (base = 2s) |
|---------|------------------|
| 1 (first retry) | 2 s |
| 2 | 4 s |
| 3 | 8 s |
| 4 | 16 s |

### `DefaultMaxAttempts`

Global retry limit before a job is dead-lettered. Default: 3.

```csharp
services.AddScheduling(opt => opt.DefaultMaxAttempts = 5);
```

Override per job type with `[Job(MaxAttempts = N)]`.

## Multiple Workers

The store implementations are safe for concurrent workers. `FetchPendingAsync` uses an atomic claim pattern:

- **EF Core**: `ExecuteUpdateAsync` with a conditional `WHERE Status IN (Pending, Failed)` — only rows still unclaimed are updated
- **Redis**: `ITransaction` with a `WATCH` on the job key

To scale horizontally, run multiple instances of your application. Each instance runs its own `SchedulingWorkerService`. Jobs are distributed across workers by whichever instance claims them first.

## When Scheduler Overhead Matters

Scheduler overhead (polling, claiming, deserialising) is typically 1–10 ms per job depending on the store and network. This is negligible if your jobs take >100 ms each.

Overhead becomes relevant when:
- Jobs complete in <10 ms (the scheduler adds meaningful relative cost)
- You enqueue >1,000 jobs/second (polling and claiming become a bottleneck)
- You run many workers polling a single Redis or SQL instance (connection pressure)

For very high throughput, consider batching work into fewer, larger jobs rather than many small ones.
```

**Step 2: Commit**

```bash
git add docs/performance.md
git commit -m "docs: add performance.md"
```

---

### Task 15: Create docs-scheduling app in ZeroAlloc.Website

**Files:**
- Create: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website/apps/docs-scheduling/` (full directory)
- Mirror structure from `apps/docs-mediator/`

**Context:** The website monorepo is at `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website`. Each docs app is a Docusaurus 3.9.2 instance. The `docs-mediator` app is the template. The docs path must point to `../../repos/scheduling/docs` (relative to the app directory). The website uses pnpm workspaces.

**Step 1: List exact files in `apps/docs-mediator/`**

```bash
find "c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website/apps/docs-mediator" -not -path "*/node_modules/*" -not -path "*/.docusaurus/*" -type f | sort
```

Use this list to know which files to copy.

**Step 2: Create `apps/docs-scheduling/` by copying and adapting `docs-mediator`**

Copy every file from `apps/docs-mediator/` into `apps/docs-scheduling/`, then make these substitutions throughout all files:

| Find | Replace |
|------|---------|
| `ZeroAlloc.Mediator` | `ZeroAlloc.Scheduling` |
| `ZeroAlloc-Net/ZeroAlloc.Mediator` | `ZeroAlloc-Net/ZeroAlloc.Scheduling` |
| `mediator.zeroalloc.net` | `scheduling.zeroalloc.net` |
| `docs-mediator` | `docs-scheduling` |
| `../../repos/mediator/docs` | `../../repos/scheduling/docs` |
| `Zero-allocation source-generated mediator for .NET` | `Source-generated background job scheduler for .NET` |
| `ZeroAlloc.Mediator` (in title/tagline fields) | `ZeroAlloc.Scheduling` |

Key file: `docusaurus.config.ts` — change `title`, `tagline`, `url`, `projectName`, and the docs `path`.

**Step 3: Add the new app to the workspace**

Check `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website/package.json` or `pnpm-workspace.yaml` to see how apps are registered. Add `apps/docs-scheduling` following the same pattern as `apps/docs-mediator`.

**Step 4: Commit the website changes**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Website
git add apps/docs-scheduling
git commit -m "feat(scheduling): add docs-scheduling Docusaurus app"
```

---

### Task 16: Final commit in Scheduling repo

**Step 1: Run all tests to confirm nothing regressed**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Scheduling
dotnet test -v quiet
```

Expected: 48/48 pass.

**Step 2: Stage and commit any remaining unstaged files**

```bash
git status
git add -A
git commit -m "docs: complete v1 documentation and CI parity with ecosystem"
```
