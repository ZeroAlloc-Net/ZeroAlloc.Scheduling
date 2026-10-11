using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.AotSmoke;

[JsonSerializable(typeof(SendEmailJob))]
[JsonSerializable(typeof(SendEmailRequest))]
internal sealed partial class SendEmailJobJsonContext : JsonSerializerContext;
