using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.Dashboard.Blazor;

/// <summary>
/// Source-generated JSON metadata for the dashboard responses the client reads, with the web
/// defaults, which is what <c>ReadFromJsonAsync</c> used before.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JobSummary))]
[JsonSerializable(typeof(IReadOnlyList<JobEntry>))]
internal sealed partial class DashboardClientJsonContext : JsonSerializerContext
{
}
