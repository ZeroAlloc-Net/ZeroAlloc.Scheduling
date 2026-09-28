using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// Every ZASCH diagnostic is reported at the source it is about, bound to the syntax tree, so the
/// IDE can point at it and <c>#pragma warning disable</c> can suppress one case. A source marked
/// with [| and |] gives the expected spans; the markers are removed before it runs.
/// </summary>
public class DiagnosticLocationTests
{
    private const string JobBody =
        "{ public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default; }";

    [Fact]
    public void ZASCH001_IsReportedAtTheMaxAttemptsArgument()
    {
        var (source, spans) = Parse($$"""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace MyApp;
            [Job(Cron = "0 * * * *", [|MaxAttempts = 3|])]
            public sealed class SendWelcomeEmailJob : IJob, IRequest<Unit> {{JobBody}}
            """);

        var diagnostics = GeneratorTestHelper.RunOnFile(source);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("ZASCH001");
        AssertAt(diagnostic.Location, spans.Single());
    }

    [Fact]
    public void ZASCH011_IsReportedAtEachClassIdentifier()
    {
        var (source, spans) = Parse($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class [|Cleanup|] : IJob {{JobBody}}
            [Job]
            public sealed class [|CleanupJob|] : IJob {{JobBody}}
            """);

        var diagnostics = GeneratorTestHelper.RunOnFile(source);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH011");
        var ordered = diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToList();
        AssertAt(ordered[0].Location, spans[0]);
        AssertAt(ordered[1].Location, spans[1]);
    }

    [Fact]
    public void ZASCH011_OnAPartialJob_IsReportedAtTheDeclarationThatCarriesTheAttribute()
    {
        var (source, spans) = Parse($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [System.Serializable]
            public sealed partial class Cleanup { }
            [Job]
            public sealed partial class [|Cleanup|] : IJob {{JobBody}}
            [Job]
            public sealed class CleanupJob : IJob {{JobBody}}
            """);

        var diagnostics = GeneratorTestHelper.RunOnFile(source);

        var onCleanup = diagnostics.Should().HaveCount(2).And
            .ContainSingle(d => d.GetMessage(CultureInfo.InvariantCulture).StartsWith("Job types 'Cleanup'", StringComparison.Ordinal))
            .Which;
        AssertAt(onCleanup.Location, spans.Single());
    }

    [Fact]
    public void PragmaAroundOneJob_SuppressesThatZASCH001Only()
    {
        var source = $$"""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace MyApp;
            #pragma warning disable ZASCH001
            [Job(MaxAttempts = 3)]
            public sealed class QuietJob : IJob, IRequest<Unit> {{JobBody}}
            #pragma warning restore ZASCH001
            [Job(MaxAttempts = 3)]
            public sealed class LoudJob : IJob, IRequest<Unit> {{JobBody}}
            """;

        var diagnostics = GeneratorTestHelper.RunOnFile(source);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH001");
        Single(diagnostics, "QuietJob").IsSuppressed.Should().BeTrue();
        Single(diagnostics, "LoudJob").IsSuppressed.Should().BeFalse();
    }

    [Fact]
    public void PragmaAroundOneJob_SuppressesThatZASCH011Only_WhenItsSeverityIsLowered()
    {
        // A #pragma cannot suppress an error, so the rule is lowered to a warning, as an
        // .editorconfig or <WarningsNotAsErrors> would. The pragma then applies only if the
        // diagnostic is bound to the tree.
        var source = $$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            #pragma warning disable ZASCH011
            [Job]
            public sealed class Cleanup : IJob {{JobBody}}
            #pragma warning restore ZASCH011
            [Job]
            public sealed class CleanupJob : IJob {{JobBody}}
            """;

        var diagnostics = GeneratorTestHelper.RunOnFile(
            source, new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal) { ["ZASCH011"] = ReportDiagnostic.Warn });

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH011");
        Single(diagnostics, "Cleanup").IsSuppressed.Should().BeTrue();
        Single(diagnostics, "CleanupJob").IsSuppressed.Should().BeFalse();
    }

    private static Diagnostic Single(IReadOnlyList<Diagnostic> diagnostics, string typeName) =>
        diagnostics.Should().ContainSingle(d => d.GetMessage(CultureInfo.InvariantCulture)
            .StartsWith("Job type" + (d.Id == "ZASCH011" ? "s" : string.Empty) + " '" + typeName + "'", StringComparison.Ordinal))
            .Which;

    private static void AssertAt(Location location, TextSpan expected)
    {
        // A source location, bound to the tree, is what #pragma and the IDE need.
        location.Kind.Should().Be(LocationKind.SourceFile);
        location.SourceTree.Should().NotBeNull();
        location.SourceTree!.FilePath.Should().Be(GeneratorTestHelper.TestFilePath);
        location.SourceSpan.Should().Be(expected);
    }

    private static (string Source, IReadOnlyList<TextSpan> Spans) Parse(string marked)
    {
        var spans = new List<TextSpan>();
        var builder = new System.Text.StringBuilder();
        var start = -1;
        for (var i = 0; i < marked.Length; i++)
        {
            if (string.CompareOrdinal(marked, i, "[|", 0, 2) == 0) { start = builder.Length; i++; continue; }
            if (string.CompareOrdinal(marked, i, "|]", 0, 2) == 0) { spans.Add(TextSpan.FromBounds(start, builder.Length)); i++; continue; }
            builder.Append(marked[i]);
        }
        return (builder.ToString(), spans);
    }
}
