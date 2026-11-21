namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// Issue #602: the hosted service generated for a recurring job seeds it at startup and again
/// every <c>SchedulingOptions.ClaimTimeout</c> until the host stops. The snapshots pin the emitted
/// service; the shape tests check that every recurring job shape compiles with it.
/// </summary>
public sealed class RecurringReseedTests
{
    private const string Execute =
        "public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;";

    private const string StatelessSnapshot = """
        internal sealed class PruneJobRecurringStartup : global::Microsoft.Extensions.Hosting.BackgroundService
        {
            private readonly global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
            private readonly global::Microsoft.Extensions.Options.IOptions<global::ZeroAlloc.Scheduling.SchedulingOptions> _options;
            private readonly global::Microsoft.Extensions.Logging.ILogger<PruneJobRecurringStartup> _logger;
            public PruneJobRecurringStartup(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory, global::Microsoft.Extensions.Options.IOptions<global::ZeroAlloc.Scheduling.SchedulingOptions> options, global::Microsoft.Extensions.Logging.ILogger<PruneJobRecurringStartup> logger)
            { _scopeFactory = scopeFactory; _options = options; _logger = logger; }
            public override async global::System.Threading.Tasks.Task StartAsync(global::System.Threading.CancellationToken cancellationToken)
            {
                await SeedAsync(cancellationToken).ConfigureAwait(false);
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            protected override async global::System.Threading.Tasks.Task ExecuteAsync(global::System.Threading.CancellationToken stoppingToken)
            {
                var interval = _options.Value.ClaimTimeout;
                var longest = global::System.TimeSpan.FromMilliseconds(4294967294d);
                var shortest = global::System.TimeSpan.FromMilliseconds(1d);
                if (interval > longest) interval = longest;
                else if (interval < shortest) interval = shortest;
                while (true)
                {
                    try
                    {
                        await global::System.Threading.Tasks.Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                        await SeedAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (global::System.OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (global::System.Exception ex)
                    {
                        global::Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, ex, "Re-seeding recurring job {JobType} failed; it is tried again after SchedulingOptions.ClaimTimeout.", "MyApp.PruneJob");
                    }
                }
            }
            private async global::System.Threading.Tasks.Task SeedAsync(global::System.Threading.CancellationToken ct)
            {
                var cron = global::ZeroAlloc.Scheduling.EveryToCron.Convert(global::ZeroAlloc.Scheduling.Every.Hour);
                var expr = global::Cronos.CronExpression.Parse(cron, global::Cronos.CronFormat.Standard);
                var next = expr.GetNextOccurrence(global::System.DateTimeOffset.UtcNow, global::System.TimeZoneInfo.Utc);
                if (next is null) return;
                var maxAttempts = _options.Value.DefaultMaxAttempts;
                var payload = global::System.Array.Empty<byte>();
                var scope = _scopeFactory.CreateAsyncScope();
                try
                {
                    var store = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::ZeroAlloc.Scheduling.IJobStore>(scope.ServiceProvider);
                    await store.UpsertRecurringAsync("MyApp.PruneJob", payload, next.Value, cron, maxAttempts, ct).ConfigureAwait(false);
                }
                finally
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        """;

    private const string StatefulSnapshot = """
        internal sealed class ReportJobRecurringStartup : global::Microsoft.Extensions.Hosting.BackgroundService
        {
            private readonly global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
            private readonly global::ZeroAlloc.Scheduling.IJobSerializer _serializer;
            private readonly global::Microsoft.Extensions.Options.IOptions<global::ZeroAlloc.Scheduling.SchedulingOptions> _options;
            private readonly global::Microsoft.Extensions.Logging.ILogger<ReportJobRecurringStartup> _logger;
            public ReportJobRecurringStartup(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory, global::ZeroAlloc.Scheduling.IJobSerializer serializer, global::Microsoft.Extensions.Options.IOptions<global::ZeroAlloc.Scheduling.SchedulingOptions> options, global::Microsoft.Extensions.Logging.ILogger<ReportJobRecurringStartup> logger)
            { _scopeFactory = scopeFactory; _serializer = serializer; _options = options; _logger = logger; }
            public override async global::System.Threading.Tasks.Task StartAsync(global::System.Threading.CancellationToken cancellationToken)
            {
                await SeedAsync(cancellationToken).ConfigureAwait(false);
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            protected override async global::System.Threading.Tasks.Task ExecuteAsync(global::System.Threading.CancellationToken stoppingToken)
            {
                var interval = _options.Value.ClaimTimeout;
                var longest = global::System.TimeSpan.FromMilliseconds(4294967294d);
                var shortest = global::System.TimeSpan.FromMilliseconds(1d);
                if (interval > longest) interval = longest;
                else if (interval < shortest) interval = shortest;
                while (true)
                {
                    try
                    {
                        await global::System.Threading.Tasks.Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                        await SeedAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (global::System.OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (global::System.Exception ex)
                    {
                        global::Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, ex, "Re-seeding recurring job {JobType} failed; it is tried again after SchedulingOptions.ClaimTimeout.", "MyApp.ReportJob");
                    }
                }
            }
            private async global::System.Threading.Tasks.Task SeedAsync(global::System.Threading.CancellationToken ct)
            {
                var cron = "0 3 * * *";
                var expr = global::Cronos.CronExpression.Parse(cron, global::Cronos.CronFormat.Standard);
                var next = expr.GetNextOccurrence(global::System.DateTimeOffset.UtcNow, global::System.TimeZoneInfo.Utc);
                if (next is null) return;
                var maxAttempts = 5;
                var payload = _serializer.Serialize(new global::MyApp.ReportJob());
                var scope = _scopeFactory.CreateAsyncScope();
                try
                {
                    var store = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::ZeroAlloc.Scheduling.IJobStore>(scope.ServiceProvider);
                    await store.UpsertRecurringAsync("MyApp.ReportJob", payload, next.Value, cron, maxAttempts, ct).ConfigureAwait(false);
                }
                finally
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        """;

    [Fact]
    public void StatelessRecurringJob_Snapshot()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(Every = Every.Hour)]
            public sealed class PruneJob : IJob { {{Execute}} }
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        StartupClass(sources.Should().ContainSingle().Subject, "PruneJobRecurringStartup").Should().Be(StatelessSnapshot);
    }

    [Fact]
    public void StatefulRecurringJob_WithMaxAttempts_Snapshot()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(Cron = "0 3 * * *", MaxAttempts = 5)]
            public sealed class ReportJob : IJob { public int Days { get; set; } = 7; {{Execute}} }
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        StartupClass(sources.Should().ContainSingle().Subject, "ReportJobRecurringStartup").Should().Be(StatefulSnapshot);
    }

    [Theory]
    [InlineData("[Job(Cron = \"0 * * * *\")] public sealed class Job1 : IJob { " + Execute + " }")]
    [InlineData("[Job(Every = Every.Minute)] internal sealed class Job1 : IJob { " + Execute + " }")]
    [InlineData("[Job(Every = Every.Day, MaxAttempts = 2)] public sealed class Job1 : IJob { public string? Name { get; set; } " + Execute + " }")]
    [InlineData("[Job(Cron = \"*/5 * * * *\")] public sealed class Job1 : IJob, ZeroAlloc.Mediator.IRequest<ZeroAlloc.Mediator.Unit> { " + Execute + " }")]
    public void EveryRecurringShape_CompilesWithThePeriodicSeed(string job)
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            {{job}}
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().Contain("internal sealed class Job1RecurringStartup : global::Microsoft.Extensions.Hosting.BackgroundService");
        source.Should().Contain("public override async global::System.Threading.Tasks.Task StartAsync(");
        source.Should().Contain("var interval = _options.Value.ClaimTimeout;");
        source.Should().Contain("await SeedAsync(stoppingToken).ConfigureAwait(false);");
        source.Should().Contain("builder.Services.AddHostedService<Job1RecurringStartup>();");
        source.Should().Contain("again every <see cref=\"global::ZeroAlloc.Scheduling.SchedulingOptions.ClaimTimeout\"/> while the host runs.");
    }

    [Fact]
    public void OneOffJob_GetsNoPeriodicSeed()
    {
        var (sources, _, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job] public sealed class Job1 : IJob { {{Execute}} }
            """);

        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().NotContain("RecurringStartup");
        source.Should().NotContain("ClaimTimeout");
    }

    // The generated startup class, from its declaration to its closing brace, with Unix line ends.
    private static string StartupClass(string source, string name)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = source.IndexOf($"internal sealed class {name} ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf("\n}\n", start, StringComparison.Ordinal);
        end.Should().BePositive();
        return source.Substring(start, end + 2 - start);
    }
}
