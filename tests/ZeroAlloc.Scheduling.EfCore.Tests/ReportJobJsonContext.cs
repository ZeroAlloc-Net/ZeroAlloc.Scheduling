using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

[JsonSerializable(typeof(ReportJob))]
internal sealed partial class ReportJobJsonContext : JsonSerializerContext;
