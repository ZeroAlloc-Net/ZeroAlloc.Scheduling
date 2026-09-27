using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.Tests;

[JsonSerializable(typeof(Ping))]
internal sealed partial class PingJsonContext : JsonSerializerContext;
