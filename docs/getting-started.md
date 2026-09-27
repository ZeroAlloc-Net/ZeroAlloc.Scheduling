---
id: getting-started
title: Getting Started
slug: /
description: Install ZeroAlloc.Scheduling and schedule your first job in under five minutes.
sidebar_position: 1
---

# Getting Started

ZeroAlloc.Scheduling is a background job scheduler for .NET 8 and .NET 10. You decorate a class with `[Job]`, and the Roslyn source generator emits the executor, DI registration, and optional recurring startup for you at build time — no reflection, no convention scanning, no `IServiceCollection.Scan`.

Upgrading from 1.x? See [Migrating to v2](migrating-to-v2.md): 2.0 makes you choose a job serializer and removes the 1.x aliases.

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

Implement `IJob` and decorate with `[Job]`. Add `[ZeroAllocSerializable]` and a `JsonSerializerContext` so the job payload is serialized without reflection, which keeps the app trim- and NativeAOT-safe.

```csharp
using System.Text.Json.Serialization;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Serialisation;

[Job]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
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

[JsonSerializable(typeof(SendWelcomeEmailJob))]
internal sealed partial class SendWelcomeEmailJobJsonContext : JsonSerializerContext;
```

### Step 2 — Register

The generator emits `AddSendWelcomeEmailJob()`. Call it alongside `AddScheduling` and your chosen backend, and choose the job serializer. `AddSerializerDispatcher()` is emitted into your assembly by the ZeroAlloc.Serialisation generator, which comes with `ZeroAlloc.Scheduling`.

```csharp
builder.Services.AddSerializerDispatcher();   // AOT-safe job serializer
builder.Services
    .AddScheduling()
    .WithInMemoryStore()
    .AddSendWelcomeEmailJob();
```

`AddScheduling()` does not pick a serializer for you. To use reflection-based System.Text.Json instead, leave out `[ZeroAllocSerializable]` and `AddSerializerDispatcher()` and chain `.WithSystemTextJsonSerializer()`; that call warns under trimming or NativeAOT. With neither, the host fails to start with a message naming both options.

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
