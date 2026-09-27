using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.AotSmoke;

[JsonSerializable(typeof(SendEmailJob))]
internal sealed partial class SendEmailJobJsonContext : JsonSerializerContext;
