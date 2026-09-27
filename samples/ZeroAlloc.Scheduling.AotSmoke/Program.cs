using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.AotSmoke;
using ZeroAlloc.Scheduling.InMemory;

// Exercises the documented registration under PublishAot=true: AddSerializerDispatcher() plus
// AddScheduling(), with no trim or AOT suppression anywhere. A job goes through IScheduler into
// the in-memory store, and the worker claims it and hands it to the generator-emitted executor,
// which deserializes it through the ZeroAlloc.Serialisation dispatcher.

var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
builder.Services.AddLogging();
builder.Services.AddSingleton<DeliverySink>();
builder.Services.AddSerializerDispatcher();
builder.Services
    .AddScheduling(o => o.PollingInterval = TimeSpan.FromMilliseconds(50))
    .WithInMemoryStore()
    .AddSendEmailJob();

using var host = builder.Build();

var serializer = host.Services.GetRequiredService<IJobSerializer>();
if (serializer is not DispatchingJobSerializer)
    return Fail($"expected DispatchingJobSerializer, got {serializer.GetType().Name}");

await host.StartAsync().ConfigureAwait(false);
string delivered;
try
{
    var scope = host.Services.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        await scope.ServiceProvider.GetRequiredService<IScheduler>()
            .EnqueueAsync(new SendEmailJob { To = "aot@example.com" }, CancellationToken.None)
            .ConfigureAwait(false);
    }

    delivered = await host.Services.GetRequiredService<DeliverySink>().Delivered.Task
        .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
}
catch (TimeoutException)
{
    return Fail("the worker did not run the job within 10 seconds");
}
finally
{
    await host.StopAsync().ConfigureAwait(false);
}

if (!string.Equals(delivered, "aot@example.com", StringComparison.Ordinal))
    return Fail($"expected aot@example.com, got {delivered}");

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string reason)
{
    Console.Error.WriteLine($"AOT smoke: FAIL - {reason}");
    return 1;
}
