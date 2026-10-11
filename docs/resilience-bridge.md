---
id: resilience-bridge
title: Resilience Bridge
slug: /docs/resilience-bridge
description: Wrap IJobTypeExecutor implementations in a ZeroAlloc.Resilience-generated proxy to add retry, circuit-breaker, and timeout policies.
sidebar_position: 6
---

# Resilience Bridge

The `ZeroAlloc.Scheduling.Resilience` package wraps any `IJobTypeExecutor` implementation in a `ZeroAlloc.Resilience`-generated proxy. This lets you add retry, circuit-breaker, timeout, or bulkhead policies to job execution without changing executor code.

## Installation

```bash
dotnet add package ZeroAlloc.Scheduling
dotnet add package ZeroAlloc.Scheduling.Resilience
```

`ZeroAlloc.Resilience`, which carries the proxy generator, comes in with `ZeroAlloc.Scheduling`.

## Define the Resilience Interface

Declare an interface that extends `IJobTypeExecutor` and annotates `ExecuteAsync` with resilience attributes:

```csharp
using ZeroAlloc.Scheduling;
using ZeroAlloc.Resilience;

[Retry(MaxAttempts = 3, BackoffMs = 500)]
[CircuitBreaker(MaxFailures = 5, ResetMs = 10_000)]
public interface IResilientSendReportExecutor : IJobTypeExecutor
{
}
```

The `ZeroAlloc.Resilience` generator emits an `IResilientSendReportExecutorResilienceProxy` class that implements this interface and wraps your real executor, and an `AddResilientSendReportExecutorResiliencePolicies()` extension that registers the policies the proxy runs.

## Register

```csharp
// Your executor, which the proxy wraps, and the policies the generator emitted
services.AddTransient<IResilientSendReportExecutor, SendReportExecutor>();
services.AddResilientSendReportExecutorResiliencePolicies();

// WithResilience wires the proxy as IJobTypeExecutor
services.AddScheduling()
        .WithInMemoryStore()
        .WithResilience<
            IResilientSendReportExecutor,
            IResilientSendReportExecutorResilienceProxy>();
```

`WithResilience<TExecutorInterface, TResilienceProxy>()` registers `TResilienceProxy` as `Transient` and binds it as `IJobTypeExecutor`. The proxy takes the `TExecutorInterface` registration as the executor it wraps. Do not also call the job's generated `Add{Job}Job()`: that registers a second executor for the same job type. `samples/ZeroAlloc.Scheduling.AotSmoke` runs this wiring under NativeAOT.

## Type Parameters

| Parameter | Description |
|-----------|-------------|
| `TExecutorInterface` | Your resilience interface (extends `IJobTypeExecutor`) |
| `TResilienceProxy` | The generated proxy class (implements `TExecutorInterface`) |

## How It Works

The resilience proxy is generated at compile time by the generator that ships in `ZeroAlloc.Resilience`. On every `ExecuteAsync` call, the proxy applies the declared policies before forwarding to the inner executor. The scheduling worker never knows a proxy is involved — it resolves `IJobTypeExecutor` from DI and calls `ExecuteAsync` as normal.

## Combining Policies

Multiple attributes are combined in declaration order — the first listed policy is outermost:

```csharp
[Timeout(Ms = 10_000)]
[Retry(MaxAttempts = 3, BackoffMs = 200)]
[CircuitBreaker(MaxFailures = 5, ResetMs = 15_000)]
public interface IResilientSendReportExecutor : IJobTypeExecutor { }
```

Here, timeout wraps retry, which wraps the circuit breaker — so each attempt is independently time-boxed.

## Interaction with Built-in Retry

The scheduling worker has its own retry mechanism controlled by `[Job(MaxAttempts = N)]` and `SchedulingOptions.DefaultMaxAttempts`. When using the resilience bridge, the proxy retries are transparent to the worker — the worker only sees a failure if all proxy retry attempts are exhausted. Consider setting `MaxAttempts = 1` on the job or relying entirely on the resilience proxy's retry logic to avoid double-retrying.
