using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Outbox;
using ZeroAlloc.Outbox.InMemory;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Outbox 4.0 has no fallback serializer: the application picks one. These tests cover
/// <see cref="EfCoreSchedulingServiceCollectionExtensions.WithOutboxWriter{TJob}"/> with each choice,
/// and with none.
/// </summary>
public sealed class OutboxSerializerChoiceTests
{
    [Fact]
    public async Task WithOutboxWriter_SerializerDispatcher_WritesAPayloadTheOutboxCanRead()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerializerDispatcher();
        services.AddOutbox().WithInMemoryStore();
        services.AddScheduling().WithOutboxWriter<ReportJob>();

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxSerializer>().Should().BeOfType<DispatchingOutboxSerializer>();

        var sent = new ReportJob("monthly", 3);
        var entry = await WriteAndClaimAsync(provider, sent);

        entry.TypeName.Should().Be(typeof(ReportJob).FullName);
        provider.GetRequiredService<IOutboxSerializer>().Deserialize<ReportJob>(entry.Payload)
            .Should().Be(sent);
    }

    [Fact]
    public async Task WithOutboxWriter_SystemTextJsonSerializer_WritesAPayloadTheOutboxCanRead()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutbox().WithInMemoryStore().WithSystemTextJsonSerializer();
        services.AddScheduling().WithOutboxWriter<ReportJob>();

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxSerializer>().Should().BeOfType<SystemTextJsonOutboxSerializer>();

        var sent = new ReportJob("weekly", 1);
        var entry = await WriteAndClaimAsync(provider, sent);

        provider.GetRequiredService<IOutboxSerializer>().Deserialize<ReportJob>(entry.Payload)
            .Should().Be(sent);
    }

    [Fact]
    public async Task WithOutboxWriter_NoSerializerChosen_FailsNamingBothOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutbox().WithInMemoryStore();
        services.AddScheduling().WithOutboxWriter<ReportJob>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IOutboxWriter<ReportJob>>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSerializerDispatcher*")
            .WithMessage("*WithSystemTextJsonSerializer*");
    }

    [Fact]
    public void WithOutboxWriter_CarriesNoTrimOrAotAnnotations()
    {
        // The writer only calls IOutboxSerializer, which carries no trim attributes. The
        // reflection-based serializer is an explicit opt-in that warns at its own call site, so
        // an app that chose AddSerializerDispatcher must get no IL2026 or IL3050 from here.
        var methods = typeof(EfCoreSchedulingServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "WithOutboxWriter" or "AddSchedulingOutboxWriter")
            .ToList();
        methods.Should().HaveCount(3);

        var writeAsync = typeof(EfCoreSchedulingServiceCollectionExtensions).Assembly
            .GetType("ZeroAlloc.Scheduling.EfCore.OutboxJobWriter`1", throwOnError: true)!
            .GetMethod("WriteAsync")!;

        foreach (var method in methods.Append(writeAsync))
        {
            method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>().Should().BeNull(method.ToString());
            method.GetCustomAttribute<RequiresDynamicCodeAttribute>().Should().BeNull(method.ToString());
        }
    }

    private static async Task<OutboxEntry> WriteAndClaimAsync(IServiceProvider provider, ReportJob job)
    {
        var scope = provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<ReportJob>>();
            await writer.WriteAsync(job).ConfigureAwait(false);
        }

        var lease = new OutboxLease("scheduling-tests", TimeSpan.FromMinutes(1));
        var claimed = await provider.GetRequiredService<IOutboxStore>().ClaimPendingAsync(10, lease, CancellationToken.None)
            .ConfigureAwait(false);
        return claimed.Should().ContainSingle().Subject;
    }
}
