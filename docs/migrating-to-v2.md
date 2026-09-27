---
id: migrating-to-v2
title: Migrating to v2
slug: /docs/migrating-to-v2
description: Upgrade from ZeroAlloc.Scheduling 1.x to 2.0 — choose a job serializer, adopt ZeroAlloc.Outbox 4.0, and replace the removed 1.x aliases.
sidebar_position: 10
---

# Migrating to v2

ZeroAlloc.Scheduling 2.0 bundles five breaking changes:

1. `AddScheduling()` no longer falls back to a reflection-based serializer. You choose one.
2. `ZeroAlloc.Scheduling.EfCore` depends on ZeroAlloc.Outbox 4.0, which makes the same change for the outbox serializer.
3. The 1.x aliases that have been `[Obsolete]` since the builder API arrived are removed.
4. A job type whose name ends in `Job` no longer gets the suffix twice in its generated registration method.
5. `ISchedulingBuilder.WithMediator()` is removed.

## Choose a job serializer

Up to 1.x, `AddScheduling()` picked the job serializer for you: the AOT-safe `DispatchingJobSerializer` when an `ISerializerDispatcher` was registered, and otherwise the reflection-based `DefaultJobSerializer`. That silent fallback is why `AddScheduling()` carried `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Every caller got `IL2026` and `IL3050`, even one that registered the dispatcher so the fallback could never run, and a NativeAOT app with warnings as errors could not call `AddScheduling()` without a suppression.

In 2.0 the fallback is gone and so are the attributes. `AddScheduling()` is trim- and AOT-safe, and the reflection-based serializer is an explicit opt-in that carries the attributes itself. This is the same shape as ZeroAlloc.Outbox 4.0.

### What you need to do

If you already call `services.AddSerializerDispatcher()`, or register your own `IJobSerializer`, nothing changes. Remove any `#pragma warning disable IL2026, IL3050` or `[UnconditionalSuppressMessage]` you put around `AddScheduling()`; it is no longer needed.

If you relied on the fallback, pick one:

```csharp
// AOT-safe: annotate each [Job] type with [ZeroAllocSerializable]
// and give it a JsonSerializerContext, then:
services.AddSerializerDispatcher();
services.AddScheduling().WithInMemoryStore().AddSendWelcomeEmailJob();

// Reflection-based System.Text.Json, the same serializer 1.x fell back to:
services.AddScheduling()
        .WithInMemoryStore()
        .AddSendWelcomeEmailJob()
        .WithSystemTextJsonSerializer();
```

The AOT-safe setup for a job type:

```csharp
using System.Text.Json.Serialization;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Serialisation;

[Job]
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class SendWelcomeEmailJob : IJob { ... }

[JsonSerializable(typeof(SendWelcomeEmailJob))]
internal sealed partial class SendWelcomeEmailJobJsonContext : JsonSerializerContext;
```

`AddSerializerDispatcher()` is emitted into your assembly by the ZeroAlloc.Serialisation generator, which comes with `ZeroAlloc.Scheduling`. It may run before or after `AddScheduling()`.

`WithSystemTextJsonSerializer()` uses `SystemTextJsonJobSerializer`, the 1.x `DefaultJobSerializer` under a new name. It produces the same bytes, so jobs already in a store stay readable. It carries `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so a trimmed or NativeAOT build warns at that call, and only there.

### If you do nothing

Resolving `IJobSerializer` throws an `InvalidOperationException` that names both options. Every generated job executor and recurring-job startup needs the serializer, and the scheduling worker builds the executors when the host starts, so the error appears as a failed host start, not when the first job runs.

### Precedence

When more than one serializer source is present, the first match wins:

1. `WithSystemTextJsonSerializer()`, which replaces every serializer registered before it.
2. An `IJobSerializer` the application registered before `AddScheduling()`.
3. `DispatchingJobSerializer`, when an `ISerializerDispatcher` is registered before or after `AddScheduling()`.

### Renamed

| 1.x | 2.0 |
|---|---|
| `DefaultJobSerializer` | `SystemTextJsonJobSerializer` |

## ZeroAlloc.Outbox 4.0

`ZeroAlloc.Scheduling.EfCore` now depends on ZeroAlloc.Outbox 4.0.0, up from 2.7.2. If your application references ZeroAlloc.Outbox directly, move it to 4.0 too.

`WithOutboxWriter<TJob>()` is unchanged. It still needs an `IOutboxStore` and an `IOutboxSerializer` in the container, and it never called `AddOutbox()` for you. What changes is the outbox setup your application owns: `AddOutbox()` no longer falls back to a serializer either. `services.AddSerializerDispatcher()` covers both libraries at once, because one generated dispatcher serves every `[ZeroAllocSerializable]` type in the assembly:

```csharp
services.AddSerializerDispatcher();
services.AddOutbox().WithEfCore<AppDbContext>();
services.AddScheduling()
        .WithEfCore(opt => opt.UseSqlServer(connectionString))
        .WithOutboxWriter<SendInvoiceJob>();
```

For the reflection-based choice, call `WithSystemTextJsonSerializer()` on both builders: `services.AddOutbox()` and `services.AddScheduling()` each have one.

`WithOutboxWriter<TJob>()` no longer carries `[RequiresUnreferencedCode]` or `[RequiresDynamicCode]`: the writer only calls `IOutboxSerializer`, whose trim safety depends on the serializer you chose.

Outbox 4.0 has other breaking changes, such as one outbox store per container and its own removed 1.x aliases. See the Outbox guide [Migrating to v4](https://outbox.zeroalloc.net/migrating-to-v4) and [Backends](stores.md#enqueueing-through-the-outbox).

## Removed 1.x aliases

The 1.x DI extensions have been `[Obsolete]` since the builder API arrived, and 2.0 removes them. Each has a direct replacement on the `ISchedulingBuilder` that `AddScheduling()` returns:

| Removed | Obsolete ID | Replacement |
|---|---|---|
| `services.AddSchedulingInMemory()` | `ZASCH001` | `services.AddScheduling().WithInMemoryStore()` |
| `services.AddScheduling().AddSchedulingInMemory()` | `ZASCH001` | `services.AddScheduling().WithInMemoryStore()` |
| `services.AddSchedulingEfCore(...)` | `ZASCH002` | `services.AddScheduling().WithEfCore(...)` |
| `services.AddScheduling().AddSchedulingEfCore(...)` | `ZASCH002` | `services.AddScheduling().WithEfCore(...)` |
| `services.AddSchedulingOutboxWriter<TJob>()` | `ZASCH003` | `services.AddScheduling().WithOutboxWriter<TJob>()` |
| `services.AddScheduling().AddSchedulingOutboxWriter<TJob>()` | `ZASCH003` | `services.AddScheduling().WithOutboxWriter<TJob>()` |
| `services.AddSchedulingMediator()` | `ZASCH004` | `services.AddScheduling()` |
| `services.AddScheduling().AddSchedulingMediator()` | `ZASCH004` | `services.AddScheduling()` |
| `services.AddSchedulingResilience<TInterface, TProxy>()` | `ZASCH005` | `services.AddScheduling().WithResilience<TInterface, TProxy>()` |
| `services.AddScheduling().AddSchedulingResilience<TInterface, TProxy>()` | `ZASCH005` | `services.AddScheduling().WithResilience<TInterface, TProxy>()` |
| `services.AddSchedulingRedis(connectionString)` | `ZASCH006` | `services.AddScheduling().WithRedis(connectionString)` |
| `services.AddScheduling().AddSchedulingRedis(connectionString)` | `ZASCH006` | `services.AddScheduling().WithRedis(connectionString)` |
| generated `services.Add{Job}Job()` on `IServiceCollection` | `ZASCH010` | generated `services.AddScheduling().Add{Job}Job()` |

The `IServiceCollection` forms registered only their own piece. The generated one also called `AddScheduling()` for you, so it carried the same trim warnings. The replacements hang off `AddScheduling()`, so call it once and chain everything on the builder it returns.

The obsolete IDs `ZASCH002`–`ZASCH006` and `ZASCH010` are retired and will not be reused, so a `NoWarn` entry that names them is now dead and can be deleted. `ZASCH001` was also the obsolete ID of `AddSchedulingInMemory`; from 2.0 it means only the generator warning [MaxAttempts ignored for mediator bridge job](diagnostics.md), so a `NoWarn` for `ZASCH001` now hides that warning.

## WithMediator() removed

`WithMediator()` is removed; it did nothing — delete the call. The source generator has always
registered the `MediatorJobTypeExecutor<TJob>` for a `[Job]` type that also implements
`IRequest<Unit>` through the generated `AddXxxJob()` method, so `WithMediator()` had already
become a no-op kept for source compatibility. Remove it from `AddScheduling()` chains; nothing
else changes.

## Generated registration names

Up to 1.x the generator always appended `Job` to the type name, so `SendWelcomeEmailJob` registered with `AddSendWelcomeEmailJobJob()`. The documentation showed `AddSendWelcomeEmailJob()`, which did not compile. In 2.0 a type name that already ends in `Job` does not get the suffix twice:

| Job type | 1.x | 2.0 |
|---|---|---|
| `SendWelcomeEmailJob` | `AddSendWelcomeEmailJobJob()` | `AddSendWelcomeEmailJob()` |
| `Cleanup` | `AddCleanupJob()` | `AddCleanupJob()`, unchanged |
| `ImportJOB` | `AddImportJOBJob()` | `AddImportJOBJob()`, unchanged: the match is case-sensitive |

Replace every `Add{Name}JobJob()` call with `Add{Name}Job()`. The compiler finds them all, since the old names no longer exist. The internal executor class is renamed the same way, from `SendWelcomeEmailJobJobTypeExecutor` to `SendWelcomeEmailJobTypeExecutor`.

Two job types in the same namespace whose names differ only by the suffix, such as `Cleanup` and `CleanupJob`, now map to the same method. The generator reports that as the error [ZASCH011](diagnostics.md#zasch011--two-jobs-map-to-the-same-generated-registration-method); rename one of them.
