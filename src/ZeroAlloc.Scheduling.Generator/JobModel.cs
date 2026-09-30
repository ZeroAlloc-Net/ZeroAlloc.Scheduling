namespace ZeroAlloc.Scheduling.Generator;

/// <summary>
/// A job type, equatable so the pipeline can cache it. It holds no <see cref="Microsoft.CodeAnalysis.Diagnostic"/>:
/// the diagnostics are built from it when the output is produced.
/// </summary>
/// <param name="TypeFqn">
/// The namespace and the type name, the job type name the stores persist. It drops containing
/// types and type arguments, so it is only right for a job that is neither nested nor generic.
/// </param>
/// <param name="HintName">The name of the generated file, from <see cref="HintNames.ForJob"/>.</param>
/// <param name="DisplayName">The type as C# writes it, for diagnostic messages.</param>
/// <param name="UnsupportedShape">
/// Why ZASCH012 rejects the job, "generic" or "nested in type 'X'", or null for a job the
/// generator supports.
/// </param>
/// <param name="Location">The identifier of the class declaration that carries [Job].</param>
/// <param name="MaxAttemptsIgnoredLocation">
/// Where ZASCH001 is reported, the MaxAttempts argument, or null when the rule does not apply.
/// </param>
internal sealed record JobModel(
    string? Namespace,
    string TypeName,
    string TypeFqn,
    string HintName,
    string DisplayName,
    string? UnsupportedShape,
    bool IsRecurring,
    string? CronExpression,
    string? EveryValue,
    int MaxAttempts,
    bool IsMediatorBridge,
    LocationInfo Location,
    LocationInfo? MaxAttemptsIgnoredLocation);
