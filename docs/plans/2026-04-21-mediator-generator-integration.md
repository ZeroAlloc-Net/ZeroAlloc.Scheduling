# Mediator Generator Integration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** The scheduling source generator automatically emits `MediatorJobTypeExecutor<T>` registrations for `[Job]` types that also implement `IRequest<Unit>`, so users no longer need to manually wire the DI registration.

**Architecture:** Add `bool IsMediatorBridge` to `JobModel`. Extend `TryParse` to detect `ZeroAlloc.Mediator.IRequest<ZeroAlloc.Mediator.Unit>` in the type's `AllInterfaces` using Roslyn symbol inspection (no assembly reference required). Branch in `SchedulingCodeWriter` to skip the direct executor class and register `MediatorJobTypeExecutor<T>` instead. All changes are confined to `ZeroAlloc.Scheduling.Generator` and its test project.

**Tech Stack:** Roslyn `IIncrementalGenerator`, `INamedTypeSymbol.AllInterfaces`, xUnit, FluentAssertions.

---

### Task 1: Write the failing generator test

**Files:**
- Modify: `tests/ZeroAlloc.Scheduling.Generator.Tests/ZeroAlloc.Scheduling.Generator.Tests.csproj`
- Modify: `tests/ZeroAlloc.Scheduling.Generator.Tests/GeneratorTestHelper.cs`
- Modify: `tests/ZeroAlloc.Scheduling.Generator.Tests/GeneratorTests.cs`

**Step 1: Add `ZeroAlloc.Mediator` package reference to the generator test project**

In `tests/ZeroAlloc.Scheduling.Generator.Tests/ZeroAlloc.Scheduling.Generator.Tests.csproj`, add inside the existing `<ItemGroup>` that has PackageReferences:

```xml
<PackageReference Include="ZeroAlloc.Mediator" Version="1.1.8" />
```

**Step 2: Add an overload to `GeneratorTestHelper` that accepts extra assembly locations**

The current `Run` already picks up all loaded `AppDomain` assemblies, which will now include `ZeroAlloc.Mediator` since the test project references it. No change needed to `GeneratorTestHelper.cs` — the `AppDomain.CurrentDomain.GetAssemblies()` loop already captures it.

**Step 3: Write the failing test in `GeneratorTests.cs`**

Add this test to the `GeneratorTests` class:

```csharp
[Fact]
public void MediatorBridgeJob_RegistersMediatorExecutor_NotDirectExecutor()
{
    var (source, diagnostics) = GeneratorTestHelper.Run("""
        using ZeroAlloc.Scheduling;
        using ZeroAlloc.Mediator;
        namespace MyApp;
        [Job]
        public sealed class SendWelcomeEmailJob : IJob, IRequest<Unit>
        {
            public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            public System.Threading.Tasks.ValueTask<Unit> Handle(System.Threading.CancellationToken ct) => default;
        }
        """);

    diagnostics.Should().BeEmpty();
    source.Should().Contain("MediatorJobTypeExecutor");
    source.Should().Contain("AddSendWelcomeEmailJob");
    source.Should().NotContain("SendWelcomeEmailJobJobTypeExecutor"); // no direct executor class
}
```

**Step 4: Run the test to confirm it fails**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Scheduling
dotnet test tests/ZeroAlloc.Scheduling.Generator.Tests --no-build -v quiet
```

Expected: `MediatorBridgeJob_RegistersMediatorExecutor_NotDirectExecutor` FAILS — `source` contains the direct executor, not `MediatorJobTypeExecutor`.

**Step 5: Commit the failing test**

```bash
git add tests/ZeroAlloc.Scheduling.Generator.Tests/ZeroAlloc.Scheduling.Generator.Tests.csproj
git add tests/ZeroAlloc.Scheduling.Generator.Tests/GeneratorTests.cs
git commit -m "test(generator): add failing test for mediator bridge job detection"
```

---

### Task 2: Add `IsMediatorBridge` to `JobModel`

**Files:**
- Modify: `src/ZeroAlloc.Scheduling.Generator/JobModel.cs`

**Step 1: Add the field to `JobModel`**

Replace the current record definition in `src/ZeroAlloc.Scheduling.Generator/JobModel.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Scheduling.Generator;

internal sealed record JobModel(
    string? Namespace,
    string TypeName,
    string TypeFqn,
    bool IsRecurring,
    string? CronExpression,
    string? EveryValue,
    int MaxAttempts,
    bool IsMediatorBridge,
    ImmutableArray<Diagnostic> Diagnostics);
```

**Step 2: Build to confirm the compile error in `SchedulingGenerator.cs`**

```bash
dotnet build src/ZeroAlloc.Scheduling.Generator/ZeroAlloc.Scheduling.Generator.csproj
```

Expected: Error — the `new JobModel(...)` call in `SchedulingGenerator.cs` is missing the `IsMediatorBridge` argument.

---

### Task 3: Implement Mediator bridge detection in `SchedulingGenerator`

**Files:**
- Modify: `src/ZeroAlloc.Scheduling.Generator/SchedulingGenerator.cs`

**Step 1: Add detection logic and pass `IsMediatorBridge` to `JobModel`**

In `SchedulingGenerator.cs`, add the detection block immediately after the `maxAttempts` loop (before `bool isRecurring = ...`), then pass the new flag to the `JobModel` constructor.

The full updated `TryParse` method:

```csharp
private static JobModel? TryParse(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();

    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol symbol)
        return null;

    AttributeData? jobAttr = null;
    foreach (var attr in symbol.GetAttributes())
    {
        if (string.Equals(attr.AttributeClass?.ToDisplayString(), JobAttributeFqn, System.StringComparison.Ordinal))
        {
            jobAttr = attr;
            break;
        }
    }
    if (jobAttr is null) return null;

    var ns = symbol.ContainingNamespace.IsGlobalNamespace ? null
        : symbol.ContainingNamespace.ToDisplayString();

    var fqn = symbol.ContainingNamespace.IsGlobalNamespace ? symbol.Name
        : symbol.ContainingNamespace.ToDisplayString() + "." + symbol.Name;

    string? cron = null;
    string? every = null;
    int maxAttempts = 0;

    foreach (var arg in jobAttr.NamedArguments)
    {
        if (string.Equals(arg.Key, "Cron", System.StringComparison.Ordinal)) cron = arg.Value.Value as string;
        if (string.Equals(arg.Key, "Every", System.StringComparison.Ordinal) && arg.Value.Value is int everyInt && everyInt >= 0)
            every = $"Every.{EveryIntToName(everyInt)}";
        if (string.Equals(arg.Key, "MaxAttempts", System.StringComparison.Ordinal)) maxAttempts = arg.Value.Value is int i ? i : 0;
    }

    bool isMediatorBridge = false;
    foreach (var iface in symbol.AllInterfaces)
    {
        if (iface.ContainingNamespace?.ToDisplayString() == "ZeroAlloc.Mediator"
            && iface.Name == "IRequest"
            && iface.TypeArguments.Length == 1
            && iface.TypeArguments[0].ToDisplayString() == "ZeroAlloc.Mediator.Unit")
        {
            isMediatorBridge = true;
            break;
        }
    }

    bool isRecurring = cron != null || every != null;

    return new JobModel(ns, symbol.Name, fqn, isRecurring, cron, every, maxAttempts,
        isMediatorBridge, ImmutableArray<Diagnostic>.Empty);
}
```

**Step 2: Build to confirm it compiles**

```bash
dotnet build src/ZeroAlloc.Scheduling.Generator/ZeroAlloc.Scheduling.Generator.csproj
```

Expected: Build succeeds.

---

### Task 4: Update `SchedulingCodeWriter` to branch on `IsMediatorBridge`

**Files:**
- Modify: `src/ZeroAlloc.Scheduling.Generator/SchedulingCodeWriter.cs`

**Step 1: Update `Write` to skip executor for mediator bridge types**

Replace the `Write` method body:

```csharp
public static void Write(SourceProductionContext ctx, JobModel model)
{
    var sb = new StringBuilder();
    var typeFqn = $"global::{model.TypeFqn}";
    var executorName = $"{model.TypeName}JobTypeExecutor";
    var diMethodName = $"Add{model.TypeName}Job";
    var startupName = $"{model.TypeName}RecurringStartup";

    AppendHeader(sb, model.Namespace);

    if (!model.IsMediatorBridge)
        AppendExecutor(sb, executorName, typeFqn, model.TypeFqn, model.MaxAttempts);

    if (model.IsRecurring)
        AppendRecurringStartup(sb, model, startupName, typeFqn);

    AppendDiExtension(sb, diMethodName, executorName, typeFqn, startupName,
                      model.IsRecurring, model.IsMediatorBridge);

    var hint = model.Namespace != null
        ? $"{model.Namespace}_{model.TypeName}.Scheduling.g.cs"
        : $"{model.TypeName}.Scheduling.g.cs";

    ctx.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
}
```

**Step 2: Update `AppendDiExtension` signature and body**

Replace the `AppendDiExtension` method:

```csharp
private static void AppendDiExtension(StringBuilder sb, string diMethodName, string executorName,
    string typeFqn, string startupName, bool isRecurring, bool isMediatorBridge)
{
    sb.AppendLine("public static partial class SchedulingServiceCollectionExtensions");
    sb.AppendLine("{");
    sb.AppendLine($"    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {diMethodName}(");
    sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
    sb.AppendLine("    {");

    if (isMediatorBridge)
    {
        sb.AppendLine($"        services.AddTransient<global::ZeroAlloc.Scheduling.IJobTypeExecutor,");
        sb.AppendLine($"            global::ZeroAlloc.Scheduling.Mediator.MediatorJobTypeExecutor<{typeFqn}>>();");
    }
    else
    {
        sb.AppendLine($"        services.AddTransient<global::ZeroAlloc.Scheduling.IJobTypeExecutor, {executorName}>();");
    }

    if (isRecurring)
        sb.AppendLine($"        services.AddHostedService<{startupName}>();");
    sb.AppendLine("        return services;");
    sb.AppendLine("    }");
    sb.AppendLine("}");
}
```

**Step 3: Build the generator**

```bash
dotnet build src/ZeroAlloc.Scheduling.Generator/ZeroAlloc.Scheduling.Generator.csproj
```

Expected: Build succeeds.

**Step 4: Run all tests**

```bash
dotnet test
```

Expected: All tests pass, including `MediatorBridgeJob_RegistersMediatorExecutor_NotDirectExecutor`.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Scheduling.Generator/JobModel.cs
git add src/ZeroAlloc.Scheduling.Generator/SchedulingGenerator.cs
git add src/ZeroAlloc.Scheduling.Generator/SchedulingCodeWriter.cs
git commit -m "feat(generator): detect IRequest<Unit> and emit MediatorJobTypeExecutor registration"
```

---

### Task 5: Remove the "not yet implemented" note from `MediatorSchedulingExtensions`

**Files:**
- Modify: `src/ZeroAlloc.Scheduling.Mediator/MediatorSchedulingExtensions.cs`

**Step 1: Update the XML doc**

Replace the XML doc on `AddSchedulingMediator`:

```csharp
/// <summary>
/// Registers the ZeroAlloc.Scheduling mediator bridge.
/// <para>
/// Job types decorated with <c>[Job]</c> that also implement <c>IRequest&lt;Unit&gt;</c>
/// have their <see cref="MediatorJobTypeExecutor{TJob}"/> registered automatically
/// by the source generator via the generated <c>AddXxxJob()</c> extension method.
/// </para>
/// </summary>
public static IServiceCollection AddSchedulingMediator(this IServiceCollection services)
{
    return services;
}
```

**Step 2: Run all tests to confirm nothing regressed**

```bash
dotnet test
```

Expected: All tests pass.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Scheduling.Mediator/MediatorSchedulingExtensions.cs
git commit -m "docs(mediator): update AddSchedulingMediator doc — generator integration now implemented"
```
