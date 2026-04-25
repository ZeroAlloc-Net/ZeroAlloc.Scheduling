using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Outbox;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Verifies the builder-pattern entrypoints in <see cref="EfCoreSchedulingServiceCollectionExtensions"/>:
/// <c>WithEfCore</c>, <c>WithOutboxWriter&lt;TJob&gt;</c>, and their legacy <c>AddSchedulingEfCore</c> /
/// <c>AddSchedulingOutboxWriter</c> shims.
/// </summary>
public sealed class WithEfCoreTests
{
    private sealed record SampleJob(string Name);

    [Fact]
    public void WithEfCore_RegistersJobStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

#pragma warning disable IL2026, IL3050
        services.AddScheduling()
                .WithEfCore(o => o.UseSqlite("DataSource=:memory:"));
#pragma warning restore IL2026, IL3050

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetService<IJobStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddSchedulingEfCore_LegacyShim_StillRegistersJobStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSchedulingEfCore(o => o.UseSqlite("DataSource=:memory:"));

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetService<IJobStore>().Should().NotBeNull();
    }

    [Fact]
    public void WithOutboxWriter_RegistersIOutboxWriter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore, NoopOutboxStore>();
        services.AddSingleton<IOutboxSerializer, NoopOutboxSerializer>();

#pragma warning disable IL2026, IL2091, IL3050
        services.AddScheduling()
                .WithOutboxWriter<SampleJob>();
#pragma warning restore IL2026, IL2091, IL3050

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutboxWriter<SampleJob>>().Should().NotBeNull();
    }

    [Fact]
    public void AddSchedulingOutboxWriter_LegacyShim_StillRegistersWriter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore, NoopOutboxStore>();
        services.AddSingleton<IOutboxSerializer, NoopOutboxSerializer>();

#pragma warning disable IL2026, IL2091
        services.AddSchedulingOutboxWriter<SampleJob>();
#pragma warning restore IL2026, IL2091

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutboxWriter<SampleJob>>().Should().NotBeNull();
    }

    private sealed class NoopOutboxStore : IOutboxStore
    {
        public ValueTask EnqueueAsync(string typeName, ReadOnlyMemory<byte> payload,
            System.Data.Common.DbTransaction? transaction, CancellationToken ct)
            => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<OutboxEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<OutboxEntry>>([]);

        public ValueTask MarkSucceededAsync(OutboxMessageId id, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask MarkFailedAsync(OutboxMessageId id, int retryCount, DateTimeOffset nextRetryAt, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask DeadLetterAsync(OutboxMessageId id, string error, CancellationToken ct) => ValueTask.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Trimming", "IL2026", Justification = "Test code.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("AOT", "IL3050", Justification = "Test code.")]
    private sealed class NoopOutboxSerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
            => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
            => System.Text.Json.JsonSerializer.Deserialize<T>(data.Span)!;
    }
}
