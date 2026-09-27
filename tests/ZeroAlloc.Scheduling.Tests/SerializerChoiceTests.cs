using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// Scheduling 2.0 has no fallback serializer: the application picks one, the same way
/// ZeroAlloc.Outbox 4.0 does. These tests cover each choice, the precedence between them, and
/// the startup failure when there is none.
/// </summary>
public sealed class SerializerChoiceTests
{
    [Fact]
    public async Task SerializerDispatcher_RunsAGeneratedJobEndToEnd()
    {
        using var host = BuildHost(configureBefore: services => services.AddSerializerDispatcher());

        host.Services.GetRequiredService<IJobSerializer>().Should().BeOfType<DispatchingJobSerializer>();
        (await RunPingAsync(host)).Should().Be("ping");
    }

    [Fact]
    public async Task SerializerDispatcher_RegisteredAfterAddScheduling_IsStillUsed()
    {
        using var host = BuildHost(configureAfter: services => services.AddSerializerDispatcher());

        host.Services.GetRequiredService<IJobSerializer>().Should().BeOfType<DispatchingJobSerializer>();
        (await RunPingAsync(host)).Should().Be("ping");
    }

    [Fact]
    public async Task WithSystemTextJsonSerializer_RunsAGeneratedJobEndToEnd()
    {
        using var host = BuildHost(chain: b => b.WithSystemTextJsonSerializer());

        host.Services.GetRequiredService<IJobSerializer>().Should().BeOfType<SystemTextJsonJobSerializer>();
        (await RunPingAsync(host)).Should().Be("ping");
    }

    [Fact]
    public void WithSystemTextJsonSerializer_ReplacesEverySerializerRegisteredBeforeIt()
    {
        var services = new ServiceCollection();
        services.AddSerializerDispatcher();
        services.AddSingleton<IJobSerializer, CustomSerializer>();
        services.AddScheduling().WithSystemTextJsonSerializer();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IJobSerializer>().Should().ContainSingle()
            .Which.Should().BeOfType<SystemTextJsonJobSerializer>();
    }

    [Fact]
    public void ApplicationSerializer_RegisteredBeforeAddScheduling_IsKept()
    {
        var services = new ServiceCollection();
        services.AddSerializerDispatcher();
        services.AddSingleton<IJobSerializer, CustomSerializer>();
        services.AddScheduling();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobSerializer>().Should().BeOfType<CustomSerializer>();
    }

    [Fact]
    public async Task NoSerializerChosen_HostStartFailsNamingBothOptions()
    {
        using var host = BuildHost();

        var start = () => host.StartAsync();

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*AddSerializerDispatcher()*")
            .WithMessage("*WithSystemTextJsonSerializer()*");
    }

    [Fact]
    public void NoSerializerChosen_ResolvingTheSerializerFailsNamingBothOptions()
    {
        var services = new ServiceCollection();
        services.AddScheduling();
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IJobSerializer>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSerializerDispatcher()*")
            .WithMessage("*WithSystemTextJsonSerializer()*");
    }

    [Fact]
    public void AddScheduling_CarriesNoTrimOrAotAnnotations()
    {
        var addScheduling = typeof(ZeroAlloc.Scheduling.SchedulingServiceCollectionExtensions)
            .GetMethod(nameof(ZeroAlloc.Scheduling.SchedulingServiceCollectionExtensions.AddScheduling))!;

        addScheduling.GetCustomAttribute<RequiresUnreferencedCodeAttribute>().Should().BeNull();
        addScheduling.GetCustomAttribute<RequiresDynamicCodeAttribute>().Should().BeNull();
    }

    [Fact]
    public void WithSystemTextJsonSerializer_CarriesTheTrimAndAotAnnotations()
    {
        // The reflection-based serializer is the opt-in, so the warning lands on its call site.
        var optIn = typeof(ZeroAlloc.Scheduling.SchedulingServiceCollectionExtensions)
            .GetMethod(nameof(ZeroAlloc.Scheduling.SchedulingServiceCollectionExtensions.WithSystemTextJsonSerializer))!;

        optIn.GetCustomAttribute<RequiresUnreferencedCodeAttribute>().Should().NotBeNull();
        optIn.GetCustomAttribute<RequiresDynamicCodeAttribute>().Should().NotBeNull();
    }

    private static IHost BuildHost(
        Action<IServiceCollection>? configureBefore = null,
        Func<ISchedulingBuilder, ISchedulingBuilder>? chain = null,
        Action<IServiceCollection>? configureAfter = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<PingSink>();
        configureBefore?.Invoke(builder.Services);

        var scheduling = builder.Services
            .AddScheduling(o => o.PollingInterval = TimeSpan.FromMilliseconds(20))
            .WithInMemoryStore()
            .AddPingJob();
        chain?.Invoke(scheduling);

        configureAfter?.Invoke(builder.Services);
        return builder.Build();
    }

    private static async Task<string> RunPingAsync(IHost host)
    {
        await host.StartAsync().ConfigureAwait(false);
        try
        {
            var scope = host.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await scope.ServiceProvider.GetRequiredService<IScheduler>()
                    .EnqueueAsync(new Ping("ping")).ConfigureAwait(false);
            }

            return await host.Services.GetRequiredService<PingSink>().Received.Task
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    private sealed class CustomSerializer : IJobSerializer
    {
        public byte[] Serialize<T>(T job) where T : notnull => throw new NotSupportedException();

        public T Deserialize<T>(byte[] payload) where T : notnull => throw new NotSupportedException();
    }
}
