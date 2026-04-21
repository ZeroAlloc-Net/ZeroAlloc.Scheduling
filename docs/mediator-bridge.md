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
