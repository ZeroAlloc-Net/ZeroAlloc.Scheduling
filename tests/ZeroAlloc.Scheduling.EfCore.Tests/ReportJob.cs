using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// A job payload set up for the AOT-safe outbox serializer: the ZeroAlloc.Serialisation generator
/// emits a serializer for it that routes through <see cref="ReportJobJsonContext"/>.
/// </summary>
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed record ReportJob(string Name, int Priority);
