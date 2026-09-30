using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// Roslyn compares hint names ignoring case. Of jobs whose files would differ only in case, the
/// first declared is generated and every later one reports ZASCH013 and is skipped (#317).
/// </summary>
public sealed class CaseCollisionTests
{
    private const string JobBody =
        "{ public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default; }";

    [Fact]
    public void JobsDifferingOnlyInCase_ReportZASCH013OnTheLaterOne_AndGenerateTheRest()
    {
        var source = $$"""
            using ZeroAlloc.Scheduling;
            namespace App;
            [Job] public sealed class Cleanup : IJob {{JobBody}}
            [Job] public sealed class cleanup : IJob {{JobBody}}
            [Job] public sealed class Report : IJob {{JobBody}}
            """;

        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll(source);

        var d = diagnostics.Should().ContainSingle().Which;
        d.Id.Should().Be("ZASCH013");
        d.Severity.Should().Be(DiagnosticSeverity.Error);
        d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Be(
            "Job type 'App.cleanup' needs the generated file 'App.cleanup.Scheduling.g.cs', whose name " +
            "differs only in case from the file of job type 'App.Cleanup'. Rename one of them.");
        source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length).Should().Be("cleanup");
        errors.Should().BeEmpty();
        sources.Should().HaveCount(2);
        sources.Should().Contain(s => s.Contains("AddCleanupJob(", StringComparison.Ordinal))
            .And.Contain(s => s.Contains("AddReportJob(", StringComparison.Ordinal));
        Run([CSharpSyntaxTree.ParseText(source)]).Results[0].GeneratedSources.Select(s => s.HintName)
            .Should().BeEquivalentTo("App.Cleanup.Scheduling.g.cs", "App.Report.Scheduling.g.cs");
    }

    [Fact]
    public void NamespacesDifferingOnlyInCase_ReportEveryLaterJob()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace App { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            namespace APP { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            namespace app { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            """);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH013");
        diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Should().OnlyContain(m => m.EndsWith("of job type 'App.Cleanup'. Rename one of them.", StringComparison.Ordinal));
        errors.Should().BeEmpty();
        sources.Should().ContainSingle().Which.Should().Contain("namespace App;");
    }

    [Fact]
    public void TheJobInTheEarlierFile_KeepsItsFile_WhateverTheCompilationOrder()
    {
        var later = CSharpSyntaxTree.ParseText(
            $"using ZeroAlloc.Scheduling; namespace App; [Job] public sealed class cleanup : IJob {JobBody}", path: "/src/B.cs");
        var earlier = CSharpSyntaxTree.ParseText(
            $"using ZeroAlloc.Scheduling; namespace App; [Job] public sealed class Cleanup : IJob {JobBody}", path: "/src/A.cs");

        var result = Run([later, earlier]);

        var d = result.Diagnostics.Should().ContainSingle().Which;
        d.Id.Should().Be("ZASCH013");
        d.Location.SourceTree.Should().BeSameAs(later);
        result.Results[0].GeneratedSources.Select(s => s.HintName).Should().Equal("App.Cleanup.Scheduling.g.cs");
    }

    [Fact]
    public void JobsSkippedByZASCH011_DoNotClaimTheFile()
    {
        // Cleanup and CleanupJob collide on AddCleanupJob and generate nothing, so cleanup is free.
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace App;
            [Job] public sealed class Cleanup : IJob {{JobBody}}
            [Job] public sealed class CleanupJob : IJob {{JobBody}}
            [Job] public sealed class cleanup : IJob {{JobBody}}
            """);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH011");
        errors.Should().BeEmpty();
        sources.Should().ContainSingle().Which.Should().Contain("AddcleanupJob(");
    }

    private static GeneratorDriverRunResult Run(SyntaxTree[] trees)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(trees);
        var result = CSharpGeneratorDriver.Create(new SchedulingGenerator()).RunGenerators(compilation).GetRunResult();
        result.Results.Should().ContainSingle().Which.Exception.Should().BeNull();
        return result;
    }
}
