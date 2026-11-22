using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.InMemory;

// AddPingJob exists only if the compiler loaded the generator.
var services = new ServiceCollection();
services.AddLogging();
services.AddScheduling().WithSystemTextJsonSerializer().WithInMemoryStore().AddPingJob();

using var provider = services.BuildServiceProvider();
if (provider.GetService<IScheduler>() is null)
{
    Console.Error.WriteLine("FAIL: IScheduler is not registered");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded, job registered.");
return 0;

[Job]
public sealed class PingJob : IJob
{
    public ValueTask ExecuteAsync(JobContext ctx, CancellationToken ct) => default;
}
