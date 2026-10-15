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
| Payload deserialisation (`IJobSerializer`) | per job | What the format's serializer allocates for the job, and nothing on top. A struct job is not boxed |
| Executor and job `ExecuteAsync` | per job | 0 when the job completes synchronously; an async state machine when it awaits |
| Metrics and the `scheduling.job` activity | per job | 0 with no listener attached. With a meter listener, the `job.attempt` tag reuses a cached boxed value for attempts below 16 |
| Next run of a recurring job | per job | 0. Each distinct cron expression is parsed once and cached for the life of the process |
| `MarkSucceededAsync` / `MarkFailedAsync` / `DeadLetterAsync` | per job | Store-dependent |

On top of the store, the worker allocates only the `JobContext`: a test pins that at 64 B per job, for one-off and recurring jobs alike. The in-memory store adds 168 B per one-off job and about 470 B per recurring job, which inserts its next occurrence.

Enqueueing allocates the payload and nothing else on top of the store. `DispatchingJobSerializer.Serialize` has the ZeroAlloc.Serialisation dispatcher write the job into a buffer writer the calling thread reuses, through `ISerializerDispatcher.Serialize<T>(IBufferWriter<byte>, T)`, which does not box a struct job. It then copies the bytes once into the exact-size `byte[]` that `IJobStore` persists. `DispatchingJobSerializerTests` pins `IScheduler.EnqueueAsync` at exactly that array, for a class and a struct job:

| Job | Payload | Per enqueue, up to 2.2.5 | Per enqueue now |
|-----|---------|------------------------:|----------------:|
| class `record Ping(string Message)` | 18 bytes | 360 B | 48 B, the `byte[18]` |
| struct `record struct PingValue(int, long)` | 25 bytes | 400 B | 56 B, the `byte[25]` |

Before, the dispatcher boxed a struct job, allocated an `ArrayBufferWriter<byte>` and its buffer per call, and the serializer copied the result again. A writer that grows past 64 KiB for one large payload is dropped after that call instead of being kept for the thread.

Reading a payload back allocates only what the format's serializer allocates. `DispatchingJobSerializer.Deserialize` calls `ISerializerDispatcher.Deserialize<T>(ReadOnlyMemory<byte>)`, from ZeroAlloc.Serialisation 2.8.0, which returns a struct job as itself. `DispatchingJobSerializerTests` pins `Deserialize` and the Mediator bridge's whole per-job read path, `MediatorJobTypeExecutor<T>.ExecuteAsync`, at exactly what System.Text.Json allocates for the same payload, measured in the same run:

| Job | Per read, up to 2.2.6 | Per read now | System.Text.Json alone |
|-----|------------------------:|-------------:|-----------------------:|
| struct `record struct PingValue(int, long)` | 200 B | 168 B | 168 B |

The 32 B saved is the box the object-returning `Deserialize(ReadOnlyMemory<byte>, Type)` put around the struct. The 168 B left is System.Text.Json's: it reads a record struct through its constructor, and that path allocates the state it collects the constructor arguments in. A class job reads the same as before; it was never boxed.

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
_Last refreshed: 2026-10-15. Allocations are exact. Speed is shown as same-run ratios, because the benchmark host is a shared VM; absolute timings are not published._

Coravel 6.0.2 (the de-facto in-process scheduler in .NET) and a hand-rolled `BackgroundService + Timer` baseline (the pattern most apps reach for before adopting a library). BenchmarkDotNet 0.15.8, .NET 10.0.13, Debian 13 in a KVM virtual machine on a shared host, three launches per benchmark, pinned to four vCPUs with `taskset -c 2-5`.

### vs Coravel: registration cost

| Operation | Coravel allocation | ZA.Scheduling | Coravel time, same run |
|---|---:|---:|---:|
| Single `Schedule()` / `[Job]` registration | 696 B per call | **compile-time, 0 B at runtime** | baseline |
| 100 schedule calls (queue accumulation) | 76,320 B, 109.66× one call | **compile-time, 0 B at runtime** | 15.36× one call |

**Honest reading**: Coravel's `Schedule()` is a runtime call that allocates an entry the scheduler walks every tick. ZA.Scheduling's `[Job]` registration happens at compile time — the source generator emits one direct dispatcher per attributed type. There is no equivalent runtime registration call in ZA, so the comparison is between Coravel's per-call registration cost and ZA's no-cost-at-all (the cost moved to build time). The 696 B per `Schedule()` is the realistic cost in a Coravel app that schedules jobs dynamically at startup.

The 100-call ratio is the least stable figure here. BenchmarkDotNet reports it as 15.36 with a RatioSD of 6.04; the three launches gave 14.8, 14.2 and 11.4. Both Coravel rows rebuild the scheduler before every iteration, so they run one invocation per iteration and BenchmarkDotNet warns that the iterations are short.

### vs naive `BackgroundService + Timer` baseline: dispatch overhead

| Operation | Naive direct call | ZA.Scheduling `IJob.ExecuteAsync` |
|---|---:|---:|
| Single dispatch | indistinguishable from an empty method, 0 B | indistinguishable from an empty method, 0 B |
| 100 dispatches | — | 0 B |

BenchmarkDotNet flags both single-dispatch rows as "indistinguishable from empty-method duration", in all three launches, so it computes no ratio between them. **ZA.Scheduling adds no measurable overhead over a direct method call** — the `[Job]`-attributed proxy compiles to a direct return for synchronous completions, and the JIT inlines both bodies entirely.

The takeaway: if you're considering ZA.Scheduling over a hand-rolled `Timer`, the dispatch cost is identical. The value of the abstraction is the `[Job]` attribute itself (declarative retries, cron, store-backed persistence) — not raw dispatch speed.

Reproduce from `benchmarks/ZeroAlloc.Scheduling.Benchmarks`:

```bash
taskset -c 2-5 dotnet run -c Release -- --filter '*CoravelComparisonBenchmark*' '*NaiveTimerOverheadBenchmark*' --job Default --launchCount 3
```
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
