using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// A nested or generic [Job] type is reported as ZASCH012 and generates nothing, because the
/// code generated for it could not compile (#316).
/// </summary>
public sealed class JobShapeTests
{
    private const string JobBody =
        "{ public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default; }";

    [Fact]
    public void NestedJob_ReportsZASCH012_AndGeneratesNothingForIt()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace N;
            public static class Outer { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            [Job] public sealed class Report : IJob {{JobBody}}
            """);

        var d = diagnostics.Should().ContainSingle().Which;
        d.Id.Should().Be("ZASCH012");
        d.Severity.Should().Be(DiagnosticSeverity.Error);
        d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Be(
            "Job type 'N.Outer.Cleanup' is nested in type 'N.Outer'. [Job] supports only non-generic types " +
            "declared directly in a namespace, so no code is generated for it.");
        errors.Should().BeEmpty();
        sources.Should().ContainSingle().Which.Should().Contain("AddReportJob(");
    }

    [Fact]
    public void GenericJob_ReportsZASCH012_AndGeneratesNothingForIt()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace N;
            [Job] public sealed class Box<T> : IJob {{JobBody}}
            """);

        var d = diagnostics.Should().ContainSingle().Which;
        d.Id.Should().Be("ZASCH012");
        d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Be(
            "Job type 'N.Box<T>' is generic. [Job] supports only non-generic types " +
            "declared directly in a namespace, so no code is generated for it.");
        errors.Should().BeEmpty();
        sources.Should().BeEmpty();
    }

    [Fact]
    public void SameNamedNestedJobs_ReportOnlyZASCH012()
    {
        // Before #316 these reported ZASCH011 as "Job types 'Foo' and 'Foo'". A nested job takes
        // part in no other check, so it reports no ZASCH001 either.
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace N;
            public static class A { [Job] public sealed class Foo : IJob {{JobBody}} }
            public static class B { [Job(MaxAttempts = 2)] public sealed class Foo : IJob, IRequest<Unit> {{JobBody}} }
            """);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH012");
        diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Should().Contain(m => m.StartsWith("Job type 'N.A.Foo' ", StringComparison.Ordinal))
            .And.Contain(m => m.StartsWith("Job type 'N.B.Foo' ", StringComparison.Ordinal));
        errors.Should().BeEmpty();
        sources.Should().BeEmpty();
    }

    [Fact]
    public void ZASCH012_IsReportedAtTheClassIdentifier()
    {
        var source = $$"""
            using ZeroAlloc.Scheduling;
            namespace N;
            public static class Outer { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            """;

        var d = GeneratorTestHelper.RunOnFile(source).Should().ContainSingle().Which;
        d.Location.SourceTree!.FilePath.Should().Be(GeneratorTestHelper.TestFilePath);
        source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).Should().Be("Cleanup");
    }
}
