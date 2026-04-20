namespace ZeroAlloc.Scheduling.Generator.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void ValidJob_GeneratesTypeExecutor()
    {
        var (source, diagnostics) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(MaxAttempts = 3)]
            public sealed class SendEmailJob : IJob
            {
                public required string To { get; init; }
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        diagnostics.Should().BeEmpty();
        source.Should().NotBeNull();
        source.Should().Contain("IJobTypeExecutor");
        source.Should().Contain("SendEmailJob");
        source.Should().Contain("AddSendEmailJob");
    }

    [Fact]
    public void RecurringJob_WithCron_GeneratesStartupService()
    {
        var (source, _) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(Cron = "0 * * * *")]
            public sealed class HourlyJob : IJob
            {
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        source.Should().Contain("IHostedService");
        source.Should().Contain("UpsertRecurringAsync");
    }

    [Fact]
    public void RecurringJob_WithEvery_GeneratesStartupService()
    {
        var (source, _) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(Every = Every.Hour)]
            public sealed class HourlyCleanupJob : IJob
            {
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        source.Should().Contain("IHostedService");
        source.Should().Contain("UpsertRecurringAsync");
    }
}
