using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling;

/// <summary>Lifecycle state of a scheduled job. Serialized as the member name (not the ordinal) so
/// dashboard clients and JSON API consumers receive stable, human-readable values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    DeadLetter,
}
