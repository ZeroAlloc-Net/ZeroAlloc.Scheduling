using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.AotSmoke;

// Exercise [Job]-annotated type + IJob contract under PublishAot=true. The
// generator emits an IJobTypeExecutor + AddSendEmailJob DI extension in this
// sample's namespace; we don't invoke them directly here (that's covered by
// the runtime tests) — this smoke verifies the emitted code survives ILC
// without firing IL2026/IL3050/IL2067/IL2075/IL3051.

var job = new SendEmailJob { To = "aot@example.com" };
var ctx = new JobContext
{
    JobId = Guid.NewGuid(),
    Attempt = 1,
    ScheduledAt = DateTimeOffset.UtcNow,
    Services = new EmptyServiceProvider(),
};

SendEmailJob.ResetInvocationCount();
await job.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
if (SendEmailJob.InvocationCount != 1)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — SendEmailJob.ExecuteAsync expected 1 invocation, got {SendEmailJob.InvocationCount}");
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;
