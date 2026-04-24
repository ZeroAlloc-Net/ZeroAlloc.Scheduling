using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using ZeroAlloc.Outbox;

namespace ZeroAlloc.Scheduling.EfCore;

/// <summary>
/// Implements <see cref="IOutboxWriter{TJob}"/> by serializing a job payload with
/// <see cref="IOutboxSerializer"/> and persisting it via <see cref="IOutboxStore"/>.
/// </summary>
/// <remarks>
/// Enqueuing within the same <see cref="DbTransaction"/> as the business write ensures
/// atomicity: if the surrounding transaction rolls back the outbox row is also rolled back.
/// This class is intentionally not AOT-safe — EF Core itself is not AOT-compatible.
/// </remarks>
internal sealed class OutboxJobWriter<TJob> : IOutboxWriter<TJob>
    where TJob : notnull
{
    private readonly IOutboxStore _store;
    private readonly IOutboxSerializer _serializer;

    public OutboxJobWriter(IOutboxStore store, IOutboxSerializer serializer)
    {
        _store = store;
        _serializer = serializer;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("Serialization may require dynamic code generation.")]
    public ValueTask WriteAsync(
        TJob message,
        DbTransaction? transaction = null,
        CancellationToken ct = default)
    {
        var typeName = typeof(TJob).FullName ?? typeof(TJob).Name;
        ReadOnlyMemory<byte> payload = _serializer.Serialize(message);
        return _store.EnqueueAsync(typeName, payload, transaction, ct);
    }
}
