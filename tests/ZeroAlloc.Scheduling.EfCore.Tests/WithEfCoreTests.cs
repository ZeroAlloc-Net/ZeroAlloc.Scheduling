using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Outbox;
using ZeroAlloc.Outbox.InMemory;
using ZeroAlloc.Scheduling.EfCore;

namespace ZeroAlloc.Scheduling.EfCore.Tests;

/// <summary>
/// Verifies the builder-pattern entrypoints in <see cref="EfCoreSchedulingServiceCollectionExtensions"/>:
/// <c>WithEfCore</c> and <c>WithOutboxWriter&lt;TJob&gt;</c>.
/// </summary>
public sealed class WithEfCoreTests
{
    private sealed record SampleJob(string Name);

    [Fact]
    public void WithEfCore_RegistersJobStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddScheduling()
                .WithEfCore(o => o.UseSqlite("DataSource=:memory:"));

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetService<IJobStore>().Should().NotBeNull();
    }

    [Fact]
    public void WithOutboxWriter_RegistersIOutboxWriter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerializerDispatcher();
        services.AddOutbox().WithInMemoryStore();

        services.AddScheduling()
                .WithOutboxWriter<SampleJob>();

        using var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutboxWriter<SampleJob>>().Should().NotBeNull();
    }

}
