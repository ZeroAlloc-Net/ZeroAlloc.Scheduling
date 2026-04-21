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
        source.Should().NotContain("IHostedService"); // fire-and-forget: no startup service
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

    [Fact]
    public void MediatorBridgeJob_RegistersMediatorExecutor_NotDirectExecutor()
    {
        var (source, diagnostics) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace MyApp;
            [Job]
            public sealed class SendWelcomeEmailJob : IJob, IRequest<Unit>
            {
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        diagnostics.Should().BeEmpty();
        source.Should().Contain("MediatorJobTypeExecutor");
        source.Should().Contain("AddSendWelcomeEmailJob");
        source.Should().NotContain("SendWelcomeEmailJobJobTypeExecutor"); // no direct executor class
    }

    [Fact]
    public void RecurringMediatorBridgeJob_GeneratesStartupServiceAndMediatorRegistration()
    {
        var (source, diagnostics) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace MyApp;
            [Job(Cron = "0 * * * *")]
            public sealed class HourlyReportJob : IJob, IRequest<Unit>
            {
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        diagnostics.Should().BeEmpty();
        source.Should().Contain("MediatorJobTypeExecutor");
        source.Should().Contain("AddHourlyReportJob");
        source.Should().Contain("IHostedService");         // recurring startup still emitted
        source.Should().NotContain("HourlyReportJobJobTypeExecutor"); // no direct executor
    }

    [Fact]
    public void MediatorBridgeJob_WithMaxAttempts_EmitsZASCH001Warning()
    {
        var (_, diagnostics) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace MyApp;
            [Job(MaxAttempts = 3)]
            public sealed class SendWelcomeEmailJob : IJob, IRequest<Unit>
            {
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        diagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("ZASCH001");
    }
}
