using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;

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
}
