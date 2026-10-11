using System.Text.Json.Serialization;

namespace ZeroAlloc.Scheduling.Tests;

[JsonSerializable(typeof(Ping))]
[JsonSerializable(typeof(PingValue))]
internal sealed partial class PingJsonContext : JsonSerializerContext;
