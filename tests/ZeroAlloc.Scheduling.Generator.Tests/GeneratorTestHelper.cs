using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Scheduling.Generator.Tests;

internal static class GeneratorTestHelper
{
    public static (string? GeneratedSource, IReadOnlyList<Diagnostic> Diagnostics) Run(string source)
    {
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        refs.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Scheduling.JobAttribute).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Mediator.IRequest<>).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Scheduling.Mediator.MediatorJobTypeExecutor<>).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new SchedulingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();

        var generated = result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .FirstOrDefault();

        return (generated, result.Diagnostics);
    }
}
