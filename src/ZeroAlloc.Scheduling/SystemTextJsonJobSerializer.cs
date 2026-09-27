using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ZeroAlloc.Scheduling;

/// <summary>
/// Reflection-based System.Text.Json implementation of <see cref="IJobSerializer"/>. Not trim- or
/// AOT-safe.
/// </summary>
/// <remarks>
/// Opt in with <see cref="SchedulingServiceCollectionExtensions.WithSystemTextJsonSerializer"/>.
/// Up to 1.x this class was named <c>DefaultJobSerializer</c> and was the silent fallback of
/// <c>AddScheduling()</c>. The format is unchanged, so jobs already in a store stay readable. For
/// trim- and AOT-safe serialisation, use <see cref="DispatchingJobSerializer"/> through
/// <c>services.AddSerializerDispatcher()</c> instead.
/// </remarks>
[RequiresUnreferencedCode("SystemTextJsonJobSerializer uses reflection-based System.Text.Json. For trim- and AOT-safe serialisation, call services.AddSerializerDispatcher() instead.")]
[RequiresDynamicCode("SystemTextJsonJobSerializer uses reflection-based System.Text.Json, which may need runtime code generation. For AOT-safe serialisation, call services.AddSerializerDispatcher() instead.")]
public sealed class SystemTextJsonJobSerializer : IJobSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public byte[] Serialize<T>(T job) where T : notnull
        => JsonSerializer.SerializeToUtf8Bytes(job, Options);

    /// <inheritdoc/>
    public T Deserialize<T>(byte[] payload) where T : notnull
        => JsonSerializer.Deserialize<T>(payload, Options)
           ?? throw new InvalidOperationException($"Failed to deserialize payload as {typeof(T).Name}.");
}
