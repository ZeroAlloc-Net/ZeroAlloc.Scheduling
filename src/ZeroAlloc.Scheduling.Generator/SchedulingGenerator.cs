using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Scheduling.Generator;

[Generator]
public sealed class SchedulingGenerator : IIncrementalGenerator
{
    private const string JobAttributeFqn = "ZeroAlloc.Scheduling.JobAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, ct) => TryParse(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(models, static (ctx, model) =>
        {
            bool hasErrors = false;
            foreach (var d in model.Diagnostics)
            {
                ctx.ReportDiagnostic(d);
                if (d.Severity == DiagnosticSeverity.Error) hasErrors = true;
            }
            if (!hasErrors)
                SchedulingCodeWriter.Write(ctx, model);
        });
    }

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

        bool isRecurring = cron != null || every != null;

        return new JobModel(ns, symbol.Name, fqn, isRecurring, cron, every, maxAttempts,
            ImmutableArray<Diagnostic>.Empty);
    }

    // Maps Every enum integer values to names without depending on the ZeroAlloc.Scheduling assembly.
    private static string EveryIntToName(int value) => value switch
    {
        0 => "Minute",
        1 => "FiveMinutes",
        2 => "FifteenMinutes",
        3 => "ThirtyMinutes",
        4 => "Hour",
        5 => "SixHours",
        6 => "TwelveHours",
        7 => "Day",
        8 => "Week",
        _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
