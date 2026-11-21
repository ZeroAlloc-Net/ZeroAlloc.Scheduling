using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// A job without instance fields runs in a host that chose no serializer at all, so a library can
/// ship a recurring job without depending on the host's serializer knowing its type.
/// </summary>
public sealed class StatelessJobTests
{
    [Fact]
    public async Task RecurringStatelessJob_IsSeededAndRuns_WithNoSerializerRegistered()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<TickSink>();
        builder.Services
            .AddScheduling(o => o.PollingInterval = TimeSpan.FromMilliseconds(20))
            .WithInMemoryStore()
            .AddTickJob();
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var store = host.Services.GetRequiredService<IJobStore>().Should().BeOfType<InMemoryJobStore>().Subject;
            var seeded = (await store.GetRecurringAsync()).Should().ContainSingle().Subject;
            seeded.TypeName.Should().Be(typeof(Tick).FullName);
            seeded.Payload.Should().BeEmpty("a job without state is seeded without serializing it");

            // A run due now, as the seed would be at the next minute, with the same empty payload.
            await store.EnqueueAsync(seeded.TypeName, [], DateTimeOffset.UtcNow, 3, null, default);

            await host.Services.GetRequiredService<TickSink>().Ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
