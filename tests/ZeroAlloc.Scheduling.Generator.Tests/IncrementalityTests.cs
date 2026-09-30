using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// The cached job models carry source locations, so an edit that does not touch a job must leave
/// every tracked step and output cached, and an edit that moves a job must move its diagnostic.
/// </summary>
public class IncrementalityTests
{
    private const string JobBody =
        "{ public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default; }";

    // ZASCH001 on SendWelcomeEmailJob, ZASCH011 on Cleanup and CleanupJob, ZASCH012 on the nested
    // job, ZASCH013 on hourlyJob, whose file differs from HourlyJob's only in case, and one clean job.
    private const string JobsSource = $$"""
        using ZeroAlloc.Scheduling;
        using ZeroAlloc.Mediator;
        namespace MyApp;

        [Job(MaxAttempts = 3)]
        public sealed class SendWelcomeEmailJob : IJob, IRequest<Unit> {{JobBody}}

        [Job]
        public sealed class Cleanup : IJob {{JobBody}}

        [Job]
        public sealed class CleanupJob : IJob {{JobBody}}

        [Job(Cron = "0 * * * *")]
        public sealed class HourlyJob : IJob {{JobBody}}

        public static class Outer { [Job] public sealed class Nested : IJob {{JobBody}} }

        [Job]
        public sealed class hourlyJob : IJob {{JobBody}}
        """;

    [Fact]
    public void UnrelatedEdit_LeavesEveryTrackedStepAndOutputCached()
    {
        var jobs = CSharpSyntaxTree.ParseText(JobsSource, path: "/src/Jobs.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace MyApp; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([jobs, unrelated]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SchedulingGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(SourceText.From(
                "namespace MyApp; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        // The tracking names the generator gives its steps. Roslyn's own steps rerun on every edit;
        // these must not produce a changed value.
        foreach (var name in new[] { "Jobs", "AllJobs" })
        {
            second.TrackedSteps.Should().ContainKey(name);
            AssertAllCachedOrUnchanged(name, second.TrackedSteps[name]);
        }

        second.TrackedOutputSteps.Should().NotBeEmpty();
        foreach (var step in second.TrackedOutputSteps)
            AssertAllCachedOrUnchanged(step.Key, step.Value);

        // A cached output still reports its diagnostics at the same place, bound to the tree.
        Describe(second.Diagnostics).Should().Equal(Describe(first.Diagnostics));
        second.Diagnostics.Select(d => d.Id).Should().BeEquivalentTo(["ZASCH001", "ZASCH011", "ZASCH011", "ZASCH012", "ZASCH013"]);
        second.Diagnostics.Should().OnlyContain(d => d.Location.SourceTree == jobs);
    }

    [Fact]
    public void EditAboveAJob_MovesItsDiagnostic()
    {
        var jobs = CSharpSyntaxTree.ParseText(JobsSource, path: "/src/Jobs.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([jobs]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SchedulingGenerator());
        driver = driver.RunGenerators(compilation);
        var before = driver.GetRunResult().Diagnostics.Should().ContainSingle(d => d.Id == "ZASCH001").Which;

        var moved = jobs.WithChangedText(SourceText.From(
            JobsSource.Replace("namespace MyApp;", "namespace MyApp;\n\n// two\n// more lines", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(jobs, moved));
        var after = driver.GetRunResult().Diagnostics.Should().ContainSingle(d => d.Id == "ZASCH001").Which;

        after.Location.GetLineSpan().StartLinePosition.Line
            .Should().Be(before.Location.GetLineSpan().StartLinePosition.Line + 3);
        after.Location.SourceTree.Should().BeSameAs(moved);
    }

    private static void AssertAllCachedOrUnchanged(string stepName, ImmutableArray<IncrementalGeneratorRunStep> runSteps)
    {
        foreach (var runStep in runSteps)
        {
            foreach (var (_, reason) in runStep.Outputs)
            {
                reason.Should().BeOneOf(
                    [IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged],
                    $"step '{stepName}' must not rerun after an unrelated edit");
            }
        }
    }

    private static List<string> Describe(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan()}").OrderBy(s => s, StringComparer.Ordinal).ToList();
}
