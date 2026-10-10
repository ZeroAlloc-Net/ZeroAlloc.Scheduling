using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.Dashboard;

/// <summary>
/// Source-generated JSON metadata for the dashboard responses, written with the web defaults
/// (camelCase), which is what <c>Results.Ok</c> used before.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JobSummary))]
[JsonSerializable(typeof(IReadOnlyList<JobEntry>))]
internal sealed partial class DashboardJsonContext : JsonSerializerContext
{
}
