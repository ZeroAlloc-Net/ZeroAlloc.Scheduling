using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Scheduling.Generator;

[Generator]
public sealed class SchedulingGenerator : IIncrementalGenerator
{
    private const string JobAttributeFqn = "ZeroAlloc.Scheduling.JobAttribute";

    private static readonly DiagnosticDescriptor MediatorMaxAttemptsIgnored = new(
        id: "ZASCH001",
        title: "MaxAttempts ignored for mediator bridge job",
        messageFormat: "Job type '{0}' specifies MaxAttempts={1} but implements IRequest<Unit> — MaxAttempts is not honoured for mediator bridge jobs. Remove [Job(MaxAttempts=...)] or use manual registration.",
        category: "ZeroAlloc.Scheduling",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RegistrationNameCollision = new(
        id: "ZASCH011",
        title: "Two jobs map to the same generated registration method",
        messageFormat: "Job types '{0}' and '{1}' in namespace '{2}' both map to the generated method '{3}()', because a trailing 'Job' is not appended twice. Rename one of them.",
        category: "ZeroAlloc.Scheduling",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ForAttributeWithMetadataName visits the declaration that carries [Job], once, so a
        // partial job whose other parts have attributes of their own is not parsed twice.
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                JobAttributeFqn,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => Parse(ctx, ct))
            .WithTrackingName(TrackingNames.Jobs);

        // Collected, so jobs whose generated names collide can be reported instead of emitting
        // code that cannot compile.
        context.RegisterSourceOutput(models.Collect().WithTrackingName(TrackingNames.AllJobs), static (ctx, all) =>
        {
            var byName = new Dictionary<(string Namespace, string Method), List<JobModel>>();
            foreach (var model in all)
            {
                var key = (model.Namespace ?? string.Empty, JobNames.RegistrationMethod(model.TypeName));
                if (!byName.TryGetValue(key, out var jobs))
                    byName[key] = jobs = new List<JobModel>();
                jobs.Add(model);
            }

            var colliding = new HashSet<JobModel>();
            foreach (var entry in byName)
            {
                var jobs = entry.Value;
                if (jobs.Count < 2) continue;
                jobs.Sort(static (x, y) => string.CompareOrdinal(x.TypeName, y.TypeName));
                var ns = entry.Key.Namespace.Length == 0 ? "<global>" : entry.Key.Namespace;
                for (var i = 0; i < jobs.Count; i++)
                {
                    var job = jobs[i];
                    var other = jobs[i == 0 ? 1 : 0];
                    colliding.Add(job);
                    ctx.ReportDiagnostic(Diagnostic.Create(RegistrationNameCollision,
                        job.Location.ToLocation(),
                        job.TypeName, other.TypeName, ns, entry.Key.Method));
                }
            }

            foreach (var model in all)
            {
                if (model.MaxAttemptsIgnoredLocation is { } maxAttempts)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(MediatorMaxAttemptsIgnored,
                        maxAttempts.ToLocation(), model.TypeName, model.MaxAttempts));
                }
                if (!colliding.Contains(model))
                    SchedulingCodeWriter.Write(ctx, model);
            }
        });
    }

    private static JobModel Parse(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var jobAttr = ctx.Attributes[0];

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
            if (string.Equals(iface.ContainingNamespace?.ToDisplayString(), "ZeroAlloc.Mediator", System.StringComparison.Ordinal)
                && string.Equals(iface.Name, "IRequest", System.StringComparison.Ordinal)
                && iface.TypeArguments.Length == 1
                && string.Equals(iface.TypeArguments[0].ToDisplayString(), "ZeroAlloc.Mediator.Unit", System.StringComparison.Ordinal))
            {
                isMediatorBridge = true;
                break;
            }
        }

        bool isRecurring = cron != null || every != null;
        var identifier = LocationInfo.From(((TypeDeclarationSyntax)ctx.TargetNode).Identifier);
        var maxAttemptsIgnored = isMediatorBridge && maxAttempts > 0
            ? MaxAttemptsLocation(jobAttr, identifier, ct)
            : null;

        return new JobModel(ns, symbol.Name, fqn, isRecurring, cron, every, maxAttempts,
            isMediatorBridge, identifier, maxAttemptsIgnored);
    }

    // ZASCH001 is about the MaxAttempts argument: the fix is to remove it.
    private static LocationInfo MaxAttemptsLocation(
        AttributeData jobAttr, LocationInfo fallback, System.Threading.CancellationToken ct)
    {
        if (jobAttr.ApplicationSyntaxReference?.GetSyntax(ct) is not AttributeSyntax attribute)
            return fallback;

        foreach (var argument in attribute.ArgumentList?.Arguments ?? default)
        {
            if (string.Equals(argument.NameEquals?.Name.Identifier.ValueText, "MaxAttempts", System.StringComparison.Ordinal))
                return LocationInfo.From(argument);
        }
        return LocationInfo.From(attribute);
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
