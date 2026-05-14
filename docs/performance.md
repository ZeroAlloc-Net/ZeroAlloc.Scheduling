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

## Head-to-head vs Coravel + naive Timer baseline

<!-- BENCH:START -->
_Last refreshed: 2026-05-14_

.NET 10.0.7, i9-12900HK, BenchmarkDotNet v0.15.4. Coravel 5.0.4 (the de-facto in-process scheduler in .NET) and a hand-rolled `BackgroundService + Timer` baseline (the pattern most apps reach for before adopting a library).

### vs Coravel: registration cost

| Operation | Coravel | ZA.Scheduling | Coravel allocation |
|---|---:|---:|---:|
| Single `Schedule()` / `[Job]` registration | 8,211 ns | **compile-time, 0 ns runtime** | 696 B per call |
| 100 schedule calls (queue accumulation) | 80,217 ns / 77,232 B | **compile-time, 0 ns runtime** | 110× the per-call cost |

**Honest reading**: Coravel's `Schedule()` is a runtime call that allocates an entry the scheduler walks every tick. ZA.Scheduling's `[Job]` registration happens at compile time — the source generator emits one direct dispatcher per attributed type. There is no equivalent runtime registration call in ZA, so the comparison is between Coravel's per-call registration cost and ZA's no-cost-at-all (the cost moved to build time). The "8,211 ns / 696 B per Schedule" is the realistic cost in a Coravel app that schedules jobs dynamically at startup.

### vs naive `BackgroundService + Timer` baseline: dispatch overhead

| Operation | Naive direct call | ZA.Scheduling `IJob.ExecuteAsync` |
|---|---:|---:|
| Single dispatch | 0.01 ns | 0.25 ns |

Both rows are in BDN's "ZeroMeasurement" range (warning: "indistinguishable from empty-method duration"). **ZA.Scheduling adds no measurable overhead over a direct method call** — the `[Job]`-attributed proxy compiles to a direct return for synchronous completions, and BDN's JIT inlines both bodies entirely.

The takeaway: if you're considering ZA.Scheduling over a hand-rolled `Timer`, the dispatch cost is identical. The value of the abstraction is the `[Job]` attribute itself (declarative retries, cron, store-backed persistence) — not raw dispatch speed.
<!-- BENCH:END -->

## Self-benchmark

The [benchmarks/ZeroAlloc.Scheduling.Benchmarks](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/tree/main/benchmarks/ZeroAlloc.Scheduling.Benchmarks) project also contains `JobExecuteBenchmark` — a measurement of the generator-emitted job dispatch cost in isolation from the store.

The benchmark calls `IJob.ExecuteAsync(ctx, ct)` in a tight loop on a pre-constructed job + context. This isolates the dispatch path from store I/O, polling, and serialisation. It is the lower bound on what scheduler overhead can be — store + network costs add on top in a real scheduler.

```bash
dotnet run --project benchmarks/ZeroAlloc.Scheduling.Benchmarks -c Release --filter "*"
```

What to watch:

- **Allocated column**: must read `0 B/op`. The dispatch is a direct virtual call on a generator-emitted class — no boxing, no `params object[]`, no closure. `JobContext` is constructed once in `[GlobalSetup]`, not per iteration
- **Mean column**: a regression from sub-5-ns dispatch suggests the generator has started emitting unnecessary object creation on the dispatch path

This benchmark does **not** cover the store-polling path; store implementations (EF Core, Redis, InMemory) each have their own allocation profile dominated by the underlying provider.
