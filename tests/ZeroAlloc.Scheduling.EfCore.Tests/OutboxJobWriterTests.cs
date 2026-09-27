using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Outbox;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Verifies that <see cref="EfCoreSchedulingServiceCollectionExtensions.AddSchedulingOutboxWriter{TJob}"/>
/// correctly wires <see cref="IOutboxWriter{TJob}"/> and that <c>WriteAsync</c> serializes
/// the job and forwards it to <see cref="IOutboxStore"/>.
/// </summary>
public sealed class OutboxJobWriterTests
{
    private sealed record SampleJob(string Name, int Priority);

    [Fact]
    public async Task AddSchedulingOutboxWriter_RegistersIOutboxWriter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore, StubOutboxStore>();
        services.AddSingleton<IOutboxSerializer, StubOutboxSerializer>();

        services.AddSchedulingOutboxWriter<SampleJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<SampleJob>>();
        writer.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteAsync_SerializesJobAndForwardsToStore()
    {
        var store = new StubOutboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IOutboxSerializer, StubOutboxSerializer>();

        services.AddSchedulingOutboxWriter<SampleJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<SampleJob>>();

        var job = new SampleJob("ProcessOrder", 1);
        await writer.WriteAsync(job);

        store.Entries.Should().HaveCount(1);
        store.Entries[0].TypeName.Should().Be(typeof(SampleJob).FullName);
        Encoding.UTF8.GetString(store.Entries[0].Payload.ToArray())
            .Should().Contain("ProcessOrder");
    }

    [Fact]
    public async Task WriteAsync_PassesTransactionToStore()
    {
        var store = new StubOutboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IOutboxSerializer, StubOutboxSerializer>();

        services.AddSchedulingOutboxWriter<SampleJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<SampleJob>>();

        // Pass a sentinel transaction — the store should receive it verbatim.
        var tx = new StubDbTransaction();
        await writer.WriteAsync(new SampleJob("Test", 0), transaction: tx);

        store.Entries.Should().HaveCount(1);
        store.Entries[0].Transaction.Should().BeSameAs(tx);
    }

    // ---- stubs ----

    /// <summary>Records enqueues. The writer must never touch the worker-side members.</summary>
    private sealed class StubOutboxStore : IOutboxStore
    {
        public List<(string TypeName, ReadOnlyMemory<byte> Payload, DbTransaction? Transaction)> Entries { get; } = [];

        public ValueTask EnqueueAsync(string typeName, ReadOnlyMemory<byte> payload,
            DbTransaction? transaction, CancellationToken ct)
        {
            Entries.Add((typeName, payload, transaction));
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<OutboxEntry>> ClaimPendingAsync(int batchSize, OutboxLease lease, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<bool> RenewLeaseAsync(OutboxMessageId id, OutboxLease lease, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<int> ReleaseLeasesAsync(IReadOnlyList<OutboxMessageId> ids, OutboxLease lease, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<bool> MarkSucceededAsync(OutboxMessageId id, OutboxLease lease, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<bool> MarkFailedAsync(
            OutboxMessageId id, int retryCount, DateTimeOffset nextRetryAt, OutboxLease lease, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<bool> DeadLetterAsync(OutboxMessageId id, string error, OutboxLease lease, CancellationToken ct)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Writes the job's <see cref="object.ToString"/> as UTF-8, enough to see that the writer
    /// serialized the job it was given. <see cref="OutboxSerializerChoiceTests"/> covers the real
    /// serializers.
    /// </summary>
    private sealed class StubOutboxSerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
            => Encoding.UTF8.GetBytes(value?.ToString() ?? string.Empty);

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new NotSupportedException();
    }

    private sealed class StubDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => null;
        public override void Commit() { }
        public override void Rollback() { }
    }
}
