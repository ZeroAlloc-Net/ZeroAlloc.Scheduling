using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ZeroAlloc.Scheduling;

/// <summary>
/// System.Text.Json implementation of <see cref="IJobSerializer"/>.
/// Uses reflection-based serialization — not AOT/trim safe.
/// </summary>
[RequiresUnreferencedCode("JSON serialization uses reflection. Use a source-generated serializer for trimming/AOT.")]
[RequiresDynamicCode("JSON serialization may require runtime code generation. Use a source-generated serializer for AOT.")]
public sealed class DefaultJobSerializer : IJobSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public byte[] Serialize<T>(T job) where T : notnull
        => JsonSerializer.SerializeToUtf8Bytes(job, Options);

    public T Deserialize<T>(byte[] payload) where T : notnull
        => JsonSerializer.Deserialize<T>(payload, Options)
           ?? throw new InvalidOperationException($"Failed to deserialize payload as {typeof(T).Name}.");
}
