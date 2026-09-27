using System.Data.Common;
using ZeroAlloc.Outbox;

namespace ZeroAlloc.Scheduling.EfCore;

/// <summary>
/// Implements <see cref="IOutboxWriter{TJob}"/> by serializing a job payload with
/// <see cref="IOutboxSerializer"/> and persisting it via <see cref="IOutboxStore"/>.
/// </summary>
/// <remarks>
/// Enqueuing within the same <see cref="DbTransaction"/> as the business write ensures
/// atomicity: if the surrounding transaction rolls back the outbox row is also rolled back.
/// The writer itself needs no reflection: whether serialization is trim- and AOT-safe depends only
/// on the <see cref="IOutboxSerializer"/> the application registered.
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
