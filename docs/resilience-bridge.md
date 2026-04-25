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
dotnet add package ZeroAlloc.Resilience
dotnet add package ZeroAlloc.Resilience.Generator
```

## Define the Resilience Interface

Declare an interface that extends `IJobTypeExecutor` and annotates `ExecuteAsync` with resilience attributes:

```csharp
using ZeroAlloc.Scheduling;
using ZeroAlloc.Resilience;

[Retry(MaxAttempts = 3, DelayMs = 500, BackoffType = BackoffType.Exponential)]
[CircuitBreaker(FailureThreshold = 5, SamplingDurationMs = 30_000, BreakDurationMs = 10_000)]
public interface IResilientSendReportExecutor : IJobTypeExecutor
{
}
```

The `ZeroAlloc.Resilience` generator emits a `ResilientSendReportExecutorProxy` class that implements this interface and wraps your real executor.

## Register

```csharp
// Register your real executor (generated or custom)
services.AddTransient<SendReportJobTypeExecutor>();

// AddSchedulingResilience wires the proxy as IJobTypeExecutor
services.AddSchedulingResilience<
    IResilientSendReportExecutor,
    ResilientSendReportExecutorProxy>();

// Standard scheduling setup
services.AddScheduling()
        .AddSchedulingInMemory()
        .AddSendReportJob();
```

`AddSchedulingResilience<TExecutorInterface, TResilienceProxy>()` registers `TResilienceProxy` as `Transient` and binds it as `IJobTypeExecutor`.

## Type Parameters

| Parameter | Description |
|-----------|-------------|
| `TExecutorInterface` | Your resilience interface (extends `IJobTypeExecutor`) |
| `TResilienceProxy` | The generated proxy class (implements `TExecutorInterface`) |

## How It Works

The resilience proxy is generated at compile time by `ZeroAlloc.Resilience.Generator`. On every `ExecuteAsync` call, the proxy applies the declared policies before forwarding to the inner executor. The scheduling worker never knows a proxy is involved — it resolves `IJobTypeExecutor` from DI and calls `ExecuteAsync` as normal.

## Combining Policies

Multiple attributes are combined in declaration order — the first listed policy is outermost:

```csharp
[Timeout(TimeoutMs = 10_000)]
[Retry(MaxAttempts = 3, DelayMs = 200)]
[CircuitBreaker(FailureThreshold = 5, SamplingDurationMs = 60_000, BreakDurationMs = 15_000)]
public interface IResilientSendReportExecutor : IJobTypeExecutor { }
```

Here, timeout wraps retry, which wraps the circuit breaker — so each attempt is independently time-boxed.

## Interaction with Built-in Retry

The scheduling worker has its own retry mechanism controlled by `[Job(MaxAttempts = N)]` and `SchedulingOptions.DefaultMaxAttempts`. When using the resilience bridge, the proxy retries are transparent to the worker — the worker only sees a failure if all proxy retry attempts are exhausted. Consider setting `MaxAttempts = 1` on the job or relying entirely on the resilience proxy's retry logic to avoid double-retrying.
