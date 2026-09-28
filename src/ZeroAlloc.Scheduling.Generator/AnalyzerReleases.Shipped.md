; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.3

### New Rules

Rule ID  | Category             | Severity | Notes
---------|----------------------|----------|--------------------------------------------
ZASCH001 | ZeroAlloc.Scheduling | Warning  | MaxAttempts ignored for mediator bridge job

## Release 2.0.0

### New Rules

Rule ID  | Category             | Severity | Notes
---------|----------------------|----------|-------------------------------------------------------
ZASCH011 | ZeroAlloc.Scheduling | Error    | Two jobs map to the same generated registration method
