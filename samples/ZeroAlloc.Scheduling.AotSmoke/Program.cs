using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.AotSmoke;

// Exercise the generator-emitted IJobTypeExecutor for SendEmailJob under
// PublishAot=true. The generator also emits AddSendEmailJob(IServiceCollection)
// which we call to wire everything through DI.

var services = new ServiceCollection();
services.AddSendEmailJob();
using var provider = services.BuildServiceProvider();

// Resolve the executor. Multiple IJobTypeExecutor implementations are possible;
// we look up the one whose TypeName matches SendEmailJob's full name.
var executors = provider.GetServices<IJobTypeExecutor>().ToArray();
var executor = executors.FirstOrDefault(e => e.TypeName.Contains("SendEmailJob", StringComparison.Ordinal));
if (executor is null)
    return Fail($"No IJobTypeExecutor registered for SendEmailJob (found {executors.Length} total)");

if (executor.MaxAttempts != 3)
    return Fail($"SendEmailJob executor.MaxAttempts expected 3, got {executor.MaxAttempts}");

// Dispatch with an empty payload — the generator-emitted executor deserialises
// SendEmailJob, resolves it from DI, and calls ExecuteAsync.
var ctx = new JobContext
{
    JobId = Guid.NewGuid(),
    Attempt = 1,
    ScheduledAt = DateTimeOffset.UtcNow,
    Services = provider,
};

SendEmailJob.ResetInvocationCount();
var emptyPayload = """{"To":"aot@example.com"}"""u8.ToArray();
await executor.ExecuteAsync(emptyPayload, ctx, CancellationToken.None).ConfigureAwait(false);

if (SendEmailJob.InvocationCount != 1)
    return Fail($"SendEmailJob.ExecuteAsync expected 1 invocation, got {SendEmailJob.InvocationCount}");

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
