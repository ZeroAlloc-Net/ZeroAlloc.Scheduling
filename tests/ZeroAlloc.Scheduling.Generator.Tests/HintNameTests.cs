using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// Generated files are named after the job's namespace and containing types, so no two jobs of
/// one compilation share a hint name (#315).
/// </summary>
public sealed class HintNameTests
{
    private const string JobBody =
        "{ public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default; }";

    [Fact]
    public void UnderscoreInNamespaceOrTypeName_DoesNotCollide()
    {
        // Joined with '_', both were named A_B_C.Scheduling.g.cs: AddSource threw, the generator
        // failed with CS8785 and no job of the project was generated.
        var source = $$"""
            using ZeroAlloc.Scheduling;
            namespace A_B { [Job] public sealed class C : IJob {{JobBody}} }
            namespace A { [Job] public sealed class B_C : IJob {{JobBody}} }
            """;

        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll(source);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        sources.Should().HaveCount(2);
        HintNamesOf(source)
            .Should().BeEquivalentTo("A_B.C.Scheduling.g.cs", "A.B_C.Scheduling.g.cs");
    }

    [Fact]
    public void JobInANamespace_IsNamedAfterTheNamespaceAndType()
    {
        HintNamesOf($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp.Jobs;
            [Job] public sealed class Cleanup : IJob {{JobBody}}
            """).Should().Equal("MyApp.Jobs.Cleanup.Scheduling.g.cs");
    }

    [Fact]
    public void JobInTheGlobalNamespace_HasNoNamespacePart()
    {
        HintNamesOf($$"""
            using ZeroAlloc.Scheduling;
            [Job] public sealed class Cleanup : IJob {{JobBody}}
            """).Should().Equal("Cleanup.Scheduling.g.cs");
    }

    [Fact]
    public void NestedAndGenericJobs_CarryTheirContainingTypesAndArity()
    {
        // Only the hint name is checked: the code generated for nested and generic jobs is a
        // separate defect, #316.
        HintNamesOf($$"""
            using ZeroAlloc.Scheduling;
            namespace App;
            public static class Outer<T> { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            [Job] public sealed class Box<T> : IJob {{JobBody}}
            """).Should().BeEquivalentTo("App.Outer`1+Cleanup.Scheduling.g.cs", "App.Box`1.Scheduling.g.cs");
    }

    [Fact]
    public void Sanitize_KeepsIdentifierCharacters_AndEscapesTheRest()
    {
        // Built in code: xUnit passes [InlineData] strings through UTF-8, which would replace a
        // lone surrogate before Sanitize sees it.
        var mathBoldA = char.ConvertFromUtf32(0x1D400);

        HintNames.Sanitize("Cleanup").Should().Be("Cleanup");
        HintNames.Sanitize("A.B+C`1").Should().Be("A.B+C`1");
        HintNames.Sanitize("Café_1").Should().Be("Café_1");
        HintNames.Sanitize("a<b>").Should().Be("a-u003Cb-u003E");
        HintNames.Sanitize("a b").Should().Be("a-u0020b");
        HintNames.Sanitize(mathBoldA + "x").Should().Be(mathBoldA + "x");
        HintNames.Sanitize(mathBoldA.Substring(0, 1) + "x").Should().Be("-uD835x");
    }

    private static IReadOnlyList<string> HintNamesOf(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation([CSharpSyntaxTree.ParseText(source)]);
        var result = CSharpGeneratorDriver.Create(new SchedulingGenerator()).RunGenerators(compilation).GetRunResult();
        result.Results.Should().ContainSingle().Which.Exception.Should().BeNull();
        return result.Results[0].GeneratedSources.Select(s => s.HintName).ToList();
    }
}
