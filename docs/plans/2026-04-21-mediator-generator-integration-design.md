# Mediator Generator Integration — Design

**Goal:** The scheduling source generator automatically emits `MediatorJobTypeExecutor<T>` registrations for `[Job]` types that also implement `IRequest<Unit>`, eliminating the need for manual DI wiring.

**Architecture:** Extend the existing `ZeroAlloc.Scheduling.Generator` with a one-flag branch. No new packages, no new attributes, same user-facing `AddXxxJob()` API.

**Tech Stack:** Roslyn incremental generator, `INamedTypeSymbol.AllInterfaces`, duck-typed FQN detection.

---

## Architecture

Three files change, all in `src/ZeroAlloc.Scheduling.Generator`:

| File | Change |
|------|--------|
| `JobModel.cs` | Add `bool IsMediatorBridge` field |
| `SchedulingGenerator.cs` | Detect `ZeroAlloc.Mediator.IRequest<ZeroAlloc.Mediator.Unit>` in `TryParse` |
| `SchedulingCodeWriter.cs` | Branch on `IsMediatorBridge` — skip executor class, use `MediatorJobTypeExecutor<T>` in DI registration |

No changes to `ZeroAlloc.Scheduling.Mediator`, core, or existing tests.

## Detection Logic

In `TryParse`, after confirming `[Job]` is present, walk `symbol.AllInterfaces` and match by FQN:

```csharp
bool isMediatorBridge = false;
foreach (var iface in symbol.AllInterfaces)
{
    if (iface.OriginalDefinition.ToDisplayString() == "ZeroAlloc.Mediator.IRequest<TResponse>"
        && iface.TypeArguments.Length == 1
        && iface.TypeArguments[0].ToDisplayString() == "ZeroAlloc.Mediator.Unit")
    {
        isMediatorBridge = true;
        break;
    }
}
```

Uses the same duck-typing pattern already in the generator for `Every` enum values. If `ZeroAlloc.Mediator` is not referenced, `AllInterfaces` won't contain the match — detection returns false silently, normal executor is emitted.

## Code Generation

When `IsMediatorBridge = false` (unchanged behaviour):
```csharp
// Emits executor class + registers it:
services.AddTransient<IJobTypeExecutor, MyJobJobTypeExecutor>();
```

When `IsMediatorBridge = true`:
```csharp
// No executor class emitted. Registration uses MediatorJobTypeExecutor<T>:
services.AddTransient<global::ZeroAlloc.Scheduling.IJobTypeExecutor,
    global::ZeroAlloc.Scheduling.Mediator.MediatorJobTypeExecutor<global::My.Namespace.MyJob>>();
```

The generated `AddMyJobJob()` method name is unchanged — user-facing API is identical.

## Testing

One new snapshot test in `ZeroAlloc.Scheduling.Generator.Tests`:
- Input: `[Job]` type implementing both `IJob` and `IRequest<Unit>`
- Snapshot verifies: no executor class in output, `MediatorJobTypeExecutor<T>` in the `AddXxxJob()` registration

The existing `MediatorBridgeTests` integration test in `ZeroAlloc.Scheduling.Tests` already covers the runtime behaviour end-to-end and continues to pass unchanged.

## Non-Goals

- No changes to `MediatorJobTypeExecutor<T>` itself
- No changes to the Mediator package's DI extensions
- No new attributes or markers required from the user
