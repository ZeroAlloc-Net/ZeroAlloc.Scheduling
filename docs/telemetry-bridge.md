---
id: telemetry-bridge
title: Telemetry Bridge
slug: /docs/telemetry-bridge
description: Wrap IJobTypeExecutor implementations in a ZeroAlloc.Telemetry-generated proxy to emit OpenTelemetry spans, counters, and histograms.
sidebar_position: 7
---

# Telemetry Bridge

The `ZeroAlloc.Scheduling.Telemetry` package wires source-generated OpenTelemetry instrumentation into the scheduling pipeline. Every registered `IJobTypeExecutor` is wrapped in a generated proxy that emits a span, a counter, and a duration histogram on every `ExecuteAsync` call. Subscribe by `ActivitySource` and `Meter` name in your OTel pipeline.

## Installation

```bash
dotnet add package ZeroAlloc.Scheduling
dotnet add package ZeroAlloc.Scheduling.Telemetry
```

## Usage

One fluent call decorates every executor the source generator (or your own code) registers:

```csharp
services.AddScheduling()
        .WithInMemoryStore()
        .AddSendReportJob()
        .WithTelemetry();
```

`WithTelemetry()` walks the `ServiceCollection`, finds every `IJobTypeExecutor` descriptor, and replaces each with one whose factory wraps the original in a source-generated `JobExecutorTelemetryInstrumented` proxy. The scheduling worker resolves `IEnumerable<IJobTypeExecutor>` and never knows a proxy is involved.

## What Gets Emitted

Every `ExecuteAsync` call emits:

| Signal | Name | Notes |
|--------|------|-------|
| Span | `scheduling.job_execute` | One per call. Status `Error` if the inner executor throws. |
| Counter | `scheduling.jobs_total` | Incremented once per call. |
| Histogram | `scheduling.job_duration_ms` | Wall-clock duration of the call in milliseconds. |

All instruments live on the same names:

- `ActivitySource` = `ZeroAlloc.Scheduling`
- `Meter` = `ZeroAlloc.Scheduling`

## Subscribing in OpenTelemetry

```csharp
services.AddOpenTelemetry()
        .WithTracing(t => t.AddSource("ZeroAlloc.Scheduling"))
        .WithMetrics(m => m.AddMeter("ZeroAlloc.Scheduling"));
```

Add your exporter of choice (`AddOtlpExporter`, `AddPrometheusExporter`, etc.) to the same builders.

## Composition with `WithResilience`

Order matters: telemetry should be **outermost** so the span captures every retry attempt, not just the final outcome. The fluent API encourages this if you put `.WithTelemetry()` last:

```csharp
services.AddScheduling()
        .WithInMemoryStore()
        .AddSendReportJob()
        .WithResilience<
            IResilientSendReportExecutor,
            ResilientSendReportExecutorProxy>()
        .WithTelemetry();
```

With this ordering the emitted span covers the full resilience policy execution — including retries, timeouts, and circuit-breaker rejections — and `scheduling.job_duration_ms` reflects the wall-clock duration of the whole policy.

## Idempotence

`WithTelemetry()` is safe to call multiple times. The descriptor walk skips any executor that is already wrapped in `JobExecutorTelemetryInstrumented`, so:

```csharp
services.AddScheduling()
        .WithInMemoryStore()
        .AddSendReportJob()
        .WithTelemetry()
        .WithTelemetry();   // no-op
```

produces a single layer of instrumentation — not nested spans from a doubly-wrapped proxy.

## How It Works

`ZeroAlloc.Telemetry` ships an `[Instrument]` source generator. The bridge package declares an interface that inherits from `IJobTypeExecutor` and re-declares `ExecuteAsync` with `[Trace]`, `[Count]`, and `[Histogram]` attributes:

```csharp
[Instrument(ActivitySource = "ZeroAlloc.Scheduling")]
public interface IJobExecutorTelemetry : IJobTypeExecutor
{
    [Trace(Name = "scheduling.job_execute")]
    [Count(Metric = "scheduling.jobs_total")]
    [Histogram(Metric = "scheduling.job_duration_ms")]
    new ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct);
}
```

The generator emits a `JobExecutorTelemetryInstrumented` class implementing this interface. Because the interface inherits from `IJobTypeExecutor`, the proxy is directly assignable to it — no separate adapter required.
