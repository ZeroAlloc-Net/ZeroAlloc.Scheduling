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
