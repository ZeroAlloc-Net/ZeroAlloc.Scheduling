using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

public class SchedulingBuilderTests
{
    [Fact]
    public void AddScheduling_ReturnsBuilder_BackedBySameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddScheduling();

        builder.Should().NotBeNull();
        builder.Should().BeAssignableTo<ISchedulingBuilder>();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void WithInMemoryStore_RegistersInMemoryStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddScheduling().WithInMemoryStore();

        var sp = services.BuildServiceProvider();
        sp.GetService<IJobStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddSchedulingInMemory_LegacyShim_StillRegistersStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSchedulingInMemory();

        services.BuildServiceProvider().GetService<IJobStore>().Should().NotBeNull();
    }
}
