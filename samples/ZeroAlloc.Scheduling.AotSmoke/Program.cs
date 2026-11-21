using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM.Migrations;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.AotSmoke;
using ZeroAlloc.Scheduling.InMemory;
using ZeroAlloc.Scheduling.Orm;
using ZeroAlloc.Scheduling.Redis;
using ZeroAlloc.Scheduling.Resilience;
using ZeroAlloc.Scheduling.Telemetry;
using StackExchange.Redis;

// Exercises every package under PublishAot=true, with no trim or AOT suppression anywhere. Each
// scenario builds its own host from the documented registration: AddSerializerDispatcher() plus
// AddScheduling(), a store, and the job's executor. A job goes through IScheduler into the store,
// and the worker claims it and hands it to the executor, which deserializes it through the
// ZeroAlloc.Serialisation dispatcher.
//
// The Redis scenario needs a server. It runs when ZA_SMOKE_REDIS holds a connection string, as it
// does in CI, and is skipped otherwise; its code is published and trim-analysed either way.

var failures = new List<string>();

await RunAsync("core + InMemory", failures, b => b.WithInMemoryStore().AddSendEmailJob()).ConfigureAwait(false);

await RunAsync("Mediator", failures, b =>
{
    b.Services.AddTransient<IRequestHandler<SendEmailRequest, Unit>, SendEmailRequestHandler>();
    b.WithInMemoryStore().AddSendEmailRequestJob();
}, enqueue: (s, ct) => s.EnqueueAsync(new SendEmailRequest { To = Recipient }, ct)).ConfigureAwait(false);

await RunAsync("Resilience", failures, b =>
{
    b.Services.AddTransient<IResilientSendEmailExecutor, ResilientSendEmailExecutor>();
    b.Services.AddResilientSendEmailExecutorResiliencePolicies();
    b.WithInMemoryStore().WithResilience<IResilientSendEmailExecutor, IResilientSendEmailExecutorResilienceProxy>();
}).ConfigureAwait(false);

await RunTelemetryAsync(failures).ConfigureAwait(false);
await RunOrmAsync(failures).ConfigureAwait(false);

var redis = Environment.GetEnvironmentVariable("ZA_SMOKE_REDIS");
if (string.IsNullOrEmpty(redis))
    Console.WriteLine("AOT smoke: Redis skipped, ZA_SMOKE_REDIS is not set");
else
{
    await RunAsync("Redis", failures, b => b.WithRedis(redis).AddSendEmailJob()).ConfigureAwait(false);
    await RunRedisClaimTimeoutAsync(redis, failures).ConfigureAwait(false);
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine($"AOT smoke: FAIL - {failure}");
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;

static async Task RunTelemetryAsync(List<string> failures)
{
    var stopped = new List<string>();
    using var listener = new ActivityListener
    {
        ShouldListenTo = source => string.Equals(source.Name, "ZeroAlloc.Scheduling", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = activity => { lock (stopped) stopped.Add(activity.OperationName); },
    };
    ActivitySource.AddActivityListener(listener);

    if (!await RunAsync("Telemetry", failures, b => b.WithInMemoryStore().AddSendEmailJob().WithTelemetry()).ConfigureAwait(false))
        return;

    lock (stopped)
    {
        if (!stopped.Contains("scheduling.job_execute"))
            failures.Add($"Telemetry: no scheduling.job_execute activity, saw [{string.Join(", ", stopped)}]");
    }
}

static async Task RunOrmAsync(List<string> failures)
{
    // A shared-cache in-memory database: every scope's connection sees the same tables, and the
    // keep-alive connection holds the database open between them.
    var connectionString = $"Data Source=aot-smoke-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    using var keepAlive = new SqliteConnection(connectionString);
    keepAlive.Open();

    var migration = new SqliteConnection(connectionString).AsAsync();
    await using (migration.ConfigureAwait(false))
    {
        await migration.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await new MigrationRunner(migration, SchedulingOrmMigrations.Sqlite, new SqliteMigrationDialect())
            .RunAsync(CancellationToken.None).ConfigureAwait(false);
    }

    await RunAsync("Orm on SQLite", failures, b =>
    {
        b.Services.AddScoped<IAsyncDbConnection>(_ => new SqliteConnection(connectionString).AsAsync());
        b.WithOrm().AddSendEmailJob();
    }).ConfigureAwait(false);
}

// Issue #600 on Redis, which has no test project: a Running job is claimed again once its claim is
// older than ClaimTimeout, the abandoned run's late outcome is ignored, and a dead-lettered recurring
// occurrence leaves room for its next occurrence. Each store here is one instance of a host.
static async Task RunRedisClaimTimeoutAsync(string redis, List<string> failures)
{
    var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis).ConfigureAwait(false);
    await using var multiplexerScope = multiplexer.ConfigureAwait(false);
    var db = multiplexer.GetDatabase();
    var before = failures.Count;

    await RunRedisReclaimAsync(db, failures).ConfigureAwait(false);
    await RunRedisDeadLetteredOccurrenceAsync(db, failures).ConfigureAwait(false);

    if (failures.Count == before)
        Console.WriteLine("AOT smoke: Redis claim timeout PASS");
}

static async Task RunRedisReclaimAsync(IDatabase db, List<string> failures)
{
    const string Name = "Redis claim timeout";
    var ct = CancellationToken.None;

    // Redis stores times in whole seconds, so the claim must be at least a second old.
    var shortTimeout = new FixedOptions(new SchedulingOptions { ClaimTimeout = TimeSpan.FromSeconds(1) });
    var longTimeout = new FixedOptions(new SchedulingOptions { ClaimTimeout = TimeSpan.FromHours(1) });
    var abandoned = new RedisJobStore(db, longTimeout);
    var typeName = $"AotSmoke.Abandoned.{Guid.NewGuid():N}";

    await abandoned.EnqueueAsync(typeName, [1], DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, ct).ConfigureAwait(false);
    var run = Find(await abandoned.FetchPendingAsync(100, ct).ConfigureAwait(false), typeName);
    if (run is null)
    {
        failures.Add($"{Name}: the job was not claimed");
        return;
    }

    if (Find(await new RedisJobStore(db, longTimeout).FetchPendingAsync(100, ct).ConfigureAwait(false), typeName) is not null)
        failures.Add($"{Name}: a claim newer than the timeout was claimed again");

    await Task.Delay(TimeSpan.FromSeconds(2.2)).ConfigureAwait(false);
    var reclaimer = new RedisJobStore(db, shortTimeout);
    var retaken = Find(await reclaimer.FetchPendingAsync(100, ct).ConfigureAwait(false), typeName);
    if (retaken is null || retaken.Attempts != run.Attempts + 1)
    {
        failures.Add($"{Name}: an expired claim was not claimed again as a new attempt");
        return;
    }

    await abandoned.MarkSucceededAsync(run.Id, null, null, 3, ct).ConfigureAwait(false);
    var status = (string?)await db.HashGetAsync($"job:{run.Id}", "status").ConfigureAwait(false);
    if (!string.Equals(status, "Running", StringComparison.Ordinal))
        failures.Add($"{Name}: the abandoned run's late outcome overwrote the new run, status {status}");

    await reclaimer.MarkSucceededAsync(retaken.Id, null, null, 3, ct).ConfigureAwait(false);
    status = (string?)await db.HashGetAsync($"job:{run.Id}", "status").ConfigureAwait(false);
    if (!string.Equals(status, "Succeeded", StringComparison.Ordinal))
        failures.Add($"{Name}: the new run's outcome was not recorded, status {status}");
}

// What the worker does when the last attempt of a recurring occurrence fails: dead-letter it, then
// upsert its next occurrence.
static async Task RunRedisDeadLetteredOccurrenceAsync(IDatabase db, List<string> failures)
{
    const string Name = "Redis dead-lettered occurrence";
    var ct = CancellationToken.None;
    var reclaimer = new RedisJobStore(db, new FixedOptions(new SchedulingOptions()));
    var recurring = $"AotSmoke.Recurring.{Guid.NewGuid():N}";
    await reclaimer.UpsertRecurringAsync(recurring, [1], DateTimeOffset.UtcNow.AddSeconds(-1), "0 0 1 1 *", 1, ct).ConfigureAwait(false);
    var occurrence = Find(await reclaimer.FetchPendingAsync(100, ct).ConfigureAwait(false), recurring);
    if (occurrence is null)
    {
        failures.Add($"{Name}: the recurring job was not claimed");
        return;
    }

    await reclaimer.DeadLetterAsync(occurrence.Id, "boom", ct).ConfigureAwait(false);
    await reclaimer.UpsertRecurringAsync(recurring, [1], DateTimeOffset.UtcNow.AddYears(1), "0 0 1 1 *", 1, ct).ConfigureAwait(false);

    var pending = await reclaimer.QueryByStatusAsync([JobStatus.Pending], 1, 1000, ct).ConfigureAwait(false);
    if (Find(pending, recurring) is null)
        failures.Add($"{Name}: no next occurrence after the dead-lettered one");
    var error = (string?)await db.HashGetAsync($"job:{occurrence.Id}", "error").ConfigureAwait(false);
    if (!string.Equals(error, "boom", StringComparison.Ordinal))
        failures.Add($"{Name}: the dead-lettered occurrence lost its error");
}

static JobEntry? Find(IReadOnlyList<JobEntry> entries, string typeName)
{
    foreach (var entry in entries)
    {
        if (string.Equals(entry.TypeName, typeName, StringComparison.Ordinal))
            return entry;
    }

    return null;
}

static async Task<bool> RunAsync(
    string name,
    List<string> failures,
    Action<ISchedulingBuilder> configure,
    Func<IScheduler, CancellationToken, ValueTask>? enqueue = null)
{
    var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
    builder.Services.AddLogging();
    builder.Services.AddSingleton<DeliverySink>();
    builder.Services.AddSerializerDispatcher();
    configure(builder.Services.AddScheduling(o => o.PollingInterval = TimeSpan.FromMilliseconds(50)));

    using var host = builder.Build();

    var serializer = host.Services.GetRequiredService<IJobSerializer>();
    if (serializer is not DispatchingJobSerializer)
    {
        failures.Add($"{name}: expected DispatchingJobSerializer, got {serializer.GetType().Name}");
        return false;
    }

    await host.StartAsync().ConfigureAwait(false);
    string delivered;
    try
    {
        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
            await (enqueue ?? EnqueueEmail)(scheduler, CancellationToken.None).ConfigureAwait(false);
        }

        delivered = await host.Services.GetRequiredService<DeliverySink>().Delivered.Task
            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
        failures.Add($"{name}: the worker did not run the job within 10 seconds");
        return false;
    }
    finally
    {
        await host.StopAsync().ConfigureAwait(false);
    }

    if (!string.Equals(delivered, Recipient, StringComparison.Ordinal))
    {
        failures.Add($"{name}: expected {Recipient}, got {delivered}");
        return false;
    }

    Console.WriteLine($"AOT smoke: {name} PASS");
    return true;
}

static ValueTask EnqueueEmail(IScheduler scheduler, CancellationToken ct)
    => scheduler.EnqueueAsync(new SendEmailJob { To = Recipient }, ct);

internal static partial class Program
{
    private const string Recipient = "aot@example.com";
}
