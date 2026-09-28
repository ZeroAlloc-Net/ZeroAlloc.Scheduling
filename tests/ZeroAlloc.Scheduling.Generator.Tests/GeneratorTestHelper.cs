using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Scheduling.Generator.Tests;

internal static class GeneratorTestHelper
{
    public static (string? GeneratedSource, IReadOnlyList<Diagnostic> Diagnostics) Run(string source)
    {
        var (sources, diagnostics, _) = RunAll(source);
        return (sources.FirstOrDefault(), diagnostics);
    }

    /// <summary>
    /// Runs the generator and returns every generated source, the generator's diagnostics and the
    /// errors of the compilation that includes the generated code.
    /// </summary>
    public static (IReadOnlyList<string> GeneratedSources, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<Diagnostic> CompilationErrors) RunAll(string source)
    {
        var compilation = CreateCompilation([CSharpSyntaxTree.ParseText(source)]);

        var generator = new SchedulingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();

        var generated = result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToList();
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (generated, result.Diagnostics, errors);
    }

    /// <summary>The file path given to the source tree by <see cref="RunOnFile"/>.</summary>
    public const string TestFilePath = "/src/Jobs.cs";

    /// <summary>
    /// Runs the generator on a source tree with the file path <see cref="TestFilePath"/> and returns
    /// its diagnostics, as the driver filters them: a <c>#pragma warning disable</c> marks the ones
    /// it covers as suppressed.
    /// </summary>
    public static IReadOnlyList<Diagnostic> RunOnFile(
        string source, IReadOnlyDictionary<string, ReportDiagnostic>? severities = null)
    {
        var compilation = CreateCompilation([CSharpSyntaxTree.ParseText(source, path: TestFilePath)]);
        if (severities is not null)
            compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(severities));

        var driver = CSharpGeneratorDriver.Create(new SchedulingGenerator()).RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }

    /// <summary>A compilation of the given trees, with every reference the generated code needs.</summary>
    public static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
    {
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        refs.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Scheduling.JobAttribute).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Mediator.IRequest<>).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Scheduling.Mediator.MediatorJobTypeExecutor<>).Assembly.Location));
        // What the generated code uses, so RunAll can compile it. The assemblies are not
        // necessarily loaded into the AppDomain yet when a test runs.
        refs.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Hosting.IHostedService).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IOptions<>).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Cronos.CronExpression).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
