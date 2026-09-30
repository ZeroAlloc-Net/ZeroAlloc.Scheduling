namespace ZeroAlloc.Scheduling.Generator;

/// <summary>
/// A job type, equatable so the pipeline can cache it. It holds no <see cref="Microsoft.CodeAnalysis.Diagnostic"/>:
/// the diagnostics are built from it when the output is produced.
/// </summary>
/// <param name="HintName">The name of the generated file, from <see cref="HintNames.ForJob"/>.</param>
/// <param name="Location">The identifier of the class declaration that carries [Job].</param>
/// <param name="MaxAttemptsIgnoredLocation">
/// Where ZASCH001 is reported, the MaxAttempts argument, or null when the rule does not apply.
/// </param>
internal sealed record JobModel(
    string? Namespace,
    string TypeName,
    string TypeFqn,
    string HintName,
    bool IsRecurring,
    string? CronExpression,
    string? EveryValue,
    int MaxAttempts,
    bool IsMediatorBridge,
    LocationInfo Location,
    LocationInfo? MaxAttemptsIgnoredLocation);
