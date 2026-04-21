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
