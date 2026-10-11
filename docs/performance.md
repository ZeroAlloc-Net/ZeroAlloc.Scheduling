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

The worker polls the store once per `PollingInterval` and runs each claimed job in turn. What that costs, per poll and per job:

| Step | When | Allocation |
|------|------|-----------|
| `IServiceScope` | per poll | One async scope for the whole batch; every job in the batch shares it |
| `FetchPendingAsync` | per poll | Store-dependent: the claimed `JobEntry` objects, the list holding them, and any network buffers |
| Executor lookup | per job | 0. One dictionary lookup on the job's type name, built once when the worker starts |
| `JobContext` | per job | 1 object, 64 B on x64. It is handed to the job, which may keep it, so it is not pooled |
| Payload deserialisation (`IJobSerializer`) | per job | The job object, plus whatever the serializer allocates |
| Executor and job `ExecuteAsync` | per job | 0 when the job completes synchronously; an async state machine when it awaits |
| Metrics and the `scheduling.job` activity | per job | 0 with no listener attached. With a meter listener, the `job.attempt` tag reuses a cached boxed value for attempts below 16 |
| Next run of a recurring job | per job | 0. Each distinct cron expression is parsed once and cached for the life of the process |
| `MarkSucceededAsync` / `MarkFailedAsync` / `DeadLetterAsync` | per job | Store-dependent |

On top of the store, the worker allocates only the `JobContext`: a test pins that at 64 B per job, for one-off and recurring jobs alike. The in-memory store adds 168 B per one-off job and about 470 B per recurring job, which inserts its next occurrence.

Enqueueing has one copy the worker path does not: `DispatchingJobSerializer.Serialize` copies the payload the ZeroAlloc.Serialisation dispatcher wrote into a fresh `byte[]`, because `IJobStore` persists `byte[]` and the dispatcher returns a `ReadOnlyMemory<byte>` over its own buffer.

The serialiser is the one you chose. `services.AddSerializerDispatcher()` selects `DispatchingJobSerializer`, which calls the serializer the ZeroAlloc.Serialisation generator emitted for each `[ZeroAllocSerializable]` job, through its `JsonSerializerContext`: no reflection, and trim- and AOT-safe. `.WithSystemTextJsonSerializer()` selects the reflection-based `SystemTextJsonJobSerializer`. See [Migrating to v2](migrating-to-v2.md#choose-a-job-serializer).

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

The [benchmarks/ZeroAlloc.Scheduling.Benchmarks](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/tree/main/benchmarks/ZeroAlloc.Scheduling.Benchmarks) project measures the scheduler apart from any store:

- `JobExecuteBenchmark` calls `IJob.ExecuteAsync(ctx, ct)` on a ready-made job and `JobContext`. It is the floor under a job's cost and touches neither the generated executor, the serializer nor the worker. It must read `0 B/op`.
- `WorkerDispatchBenchmark` wires the scheduler as an application does, with `AddSerializerDispatcher()`, `AddScheduling()` and the generated `Add{Job}Job()`, and measures three steps, for a one-off and a recurring job:
  - `GeneratedExecutor`: the generated executor deserializing the payload through `DispatchingJobSerializer`, then running the job.
  - `WorkerProcessEntry`: the worker's per-job path around it: executor lookup, `JobContext`, metrics and activity, the next cron occurrence, and the store update.
  - `WorkerProcessBatch`: one poll: the service scope, `FetchPendingAsync`, and one job.

  Its store replays one claimed entry and does nothing else, so the numbers are the scheduler's alone.

```bash
dotnet run --project benchmarks/ZeroAlloc.Scheduling.Benchmarks -c Release -- --filter "*WorkerDispatch*"
```

Allocated per operation, with no telemetry listener attached:

| Step | One-off job | Recurring job |
|------|------------:|--------------:|
| `GeneratedExecutor` | 104 B | 104 B |
| `WorkerProcessEntry` | 168 B | 168 B |
| `WorkerProcessBatch` | 328 B | 328 B |

The 104 B is the deserialized job and the serializer's work; `WorkerProcessEntry` adds the 64 B `JobContext`; `WorkerProcessBatch` adds the service scope. A recurring job costs the same as a one-off one, because its cron expression is parsed once.

What to watch: a rise in `WorkerProcessEntry` over `GeneratedExecutor` beyond the `JobContext` means the worker started allocating per job again. `WorkerAllocationTests` in the test project fails on the same regression.
