---
id: diagnostics
title: Diagnostics
slug: /docs/diagnostics
description: ZASCH001, ZASCH011, ZASCH012 and ZASCH013 compiler diagnostic reference — causes, examples, and fixes.
sidebar_position: 6
---

# Diagnostics

The ZeroAlloc.Scheduling source generator emits compiler diagnostics to catch misconfigurations at build time.

## ZASCH001 — MaxAttempts ignored for mediator bridge job

**Severity:** Warning (build succeeds)

**Reported at:** the `MaxAttempts = N` argument of `[Job]`.

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

## ZASCH011 — Two jobs map to the same generated registration method

**Severity:** Error

**Reported at:** the class name of each colliding type.

**Message:**
```
Job types 'A' and 'B' in namespace 'N' both map to the generated method 'AddXJob()', because a
trailing 'Job' is not appended twice. Rename one of them.
```

### Cause

The generated registration method is `Add{Name}Job()`, and a type name that already ends in `Job` does not get the suffix twice. Two `[Job]` types in the same namespace whose names differ only by that suffix therefore map to the same method, and to the same executor class:

```csharp
[Job] public sealed class Cleanup : IJob { ... }      // ← ZASCH011: AddCleanupJob()
[Job] public sealed class CleanupJob : IJob { ... }   // ← ZASCH011: AddCleanupJob()
```

The generated code could not compile, so the generator reports the error on both types and generates neither.

### Fix

Rename one of the types, or move it to another namespace. Each namespace gets its own generated `SchedulingServiceCollectionExtensions` class, so the same method name in two namespaces does not collide.

## ZASCH012 — [Job] type is nested or generic

**Severity:** Error

**Reported at:** the class name of the job type.

**Message:**
```
Job type 'N.Outer.Cleanup' is nested in type 'N.Outer'. [Job] supports only non-generic types
declared directly in a namespace, so no code is generated for it.
```

### Cause

The generated executor, registration method and recurring startup name the job by its namespace and type name, which is also the job type name the stores persist. That name cannot refer to a type nested in another type, and it has no type arguments for a generic type, so the generated code could not compile:

```csharp
namespace N;

public static class Outer
{
    [Job] public sealed class Cleanup : IJob { ... }   // ← ZASCH012: nested in type 'N.Outer'
}

[Job] public sealed class Box<T> : IJob { ... }        // ← ZASCH012: generic
```

The generator reports the error and generates nothing for the type. It takes part in no other check, so it reports no ZASCH001, ZASCH011 or ZASCH013 either. Other jobs are generated as usual.

### Fix

Declare the job type directly in a namespace, and give it no type parameters. A job that needs to vary by type can carry that as data in its properties.

## ZASCH013 — Two jobs need generated files whose names differ only in case

**Severity:** Error

**Reported at:** the class name of each job but the first.

**Message:**
```
Job type 'App.cleanup' needs the generated file 'App.cleanup.Scheduling.g.cs', whose name differs
only in case from the file of job type 'App.Cleanup'. Rename one of them.
```

### Cause

Each job gets a generated file named after its namespace and type, such as `App.Cleanup.Scheduling.g.cs`. The compiler compares those file names ignoring case, so two jobs whose qualified names differ only in case cannot both have one:

```csharp
namespace App;

[Job] public sealed class Cleanup : IJob { ... }
[Job] public sealed class cleanup : IJob { ... }   // ← ZASCH013
```

The job declared first, by file path and then position in the file, is generated. Every later one gets the error and is not generated. Other jobs are generated as usual. Namespaces count too: `App.Cleanup` and `app.Cleanup` collide the same way.

### Fix

Rename one of the types, or one of the namespaces, so that the names differ in more than case.

## Release tracking

`src/ZeroAlloc.Scheduling.Generator/AnalyzerReleases.Shipped.md` records the release each diagnostic first shipped in, and any later change to its category or severity. A new diagnostic goes into `AnalyzerReleases.Unshipped.md`. Changing a shipped diagnostic's severity or category, or removing it, has to be declared there under `### Changed Rules` or `### Removed Rules`, or the build fails. The same move covers every package's `PublicAPI.Unshipped.txt`: new public API goes there, and removing shipped API is declared with a `*REMOVED*` line.

Nobody moves entries by hand. When release-please opens or updates the release PR, the `ship-release-tracking` job in `.github/workflows/release-please.yml` moves everything unshipped into the Shipped files on that branch, in a `chore: mark analyzer rules and public api shipped in <version>` commit. The `release-tracking` job in CI fails a release PR while anything is still unshipped. Both use the shared [`ship-release-tracking.py`](https://github.com/ZeroAlloc-Net/.github/blob/main/scripts/ship-release-tracking.py). **Before merging a release PR,** check that it has that commit. If it doesn't, run the script with the release version from the root of the release branch and push the result.
