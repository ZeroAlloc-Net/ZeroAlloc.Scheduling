using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// The ORM and EF Core stores are scoped. A recurring job's generated seed is a singleton hosted service, so it
/// must resolve the store in a scope of its own: a host that validates scopes, as every Development host does,
/// would otherwise fail to build.
/// </summary>
public sealed class ScopedStoreTests
{
    [Fact]
    public async Task RecurringJob_IsSeeded_ThroughAScopedStore_InAHostThatValidatesScopes()
    {
        using var shared = new InMemoryJobStore();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
        builder.Services.AddLogging();
        builder.Services.AddSingleton<TickSink>();
        builder.Services.AddScoped<IJobStore>(_ => new ScopedStore(shared));
        builder.Services
            .AddScheduling(o => o.PollingInterval = TimeSpan.FromMinutes(1))
            .AddTickJob();
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var seeded = (await shared.GetRecurringAsync()).Should().ContainSingle().Subject;
            seeded.TypeName.Should().Be(typeof(Tick).FullName);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>A scoped store over one shared in-memory store, which the container must not dispose per scope.</summary>
    private sealed class ScopedStore(InMemoryJobStore inner) : IJobStore
    {
        public ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct)
            => inner.EnqueueAsync(typeName, payload, scheduledAt, maxAttempts, cronExpression, ct);

        public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
            => inner.FetchPendingAsync(batchSize, ct);

        public ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct)
            => inner.MarkSucceededAsync(id, nextRunAt, cronExpression, maxAttempts, ct);

        public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct)
            => inner.MarkFailedAsync(id, attempts, nextRetryAt, ct);

        public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct)
            => inner.DeadLetterAsync(id, error, ct);

        public ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct)
            => inner.UpsertRecurringAsync(typeName, payload, scheduledAt, cronExpression, maxAttempts, ct);
    }
}
