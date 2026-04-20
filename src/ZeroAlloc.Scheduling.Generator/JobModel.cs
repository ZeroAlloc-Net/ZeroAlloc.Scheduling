using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Scheduling.Generator;

internal sealed record JobModel(
    string? Namespace,
    string TypeName,
    string TypeFqn,
    bool IsRecurring,
    string? CronExpression,
    string? EveryValue,
    int MaxAttempts,
    ImmutableArray<Diagnostic> Diagnostics);
