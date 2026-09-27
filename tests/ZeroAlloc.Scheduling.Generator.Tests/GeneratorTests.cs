using Microsoft.CodeAnalysis;

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
    public void ValidJob_EmitsOnlyTheBuilderExtension_WithNoTrimOrAotAnnotations()
    {
        // 2.0 removed the IServiceCollection alias, obsolete as ZASCH010, which carried
        // RequiresUnreferencedCode and RequiresDynamicCode for the old fallback serializer.
        var (source, _) = GeneratorTestHelper.Run("""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class SendEmail : IJob
            {
                public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;
            }
            """);

        source.Should().Contain("ISchedulingBuilder AddSendEmailJob(");
        source.Should().NotContain("IServiceCollection AddSendEmailJob(");
        source.Should().NotContain("Obsolete");
        source.Should().NotContain("RequiresUnreferencedCode");
        source.Should().NotContain("RequiresDynamicCode");
    }

    private const string JobBody =
        "{ public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default; }";

    [Fact]
    public void JobNameEndingInJob_DoesNotDoubleTheSuffix()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class SendWelcomeEmailJob : IJob {{JobBody}}
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().Contain("ISchedulingBuilder AddSendWelcomeEmailJob(");
        source.Should().Contain("class SendWelcomeEmailJobTypeExecutor ");
        source.Should().NotContain("JobJob");
    }

    [Fact]
    public void JobNameWithoutSuffix_GetsJobAppended()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(Every = Every.Hour)]
            public sealed class Cleanup : IJob {{JobBody}}
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().Contain("ISchedulingBuilder AddCleanupJob(");
        source.Should().Contain("class CleanupJobTypeExecutor ");
        source.Should().Contain("class CleanupRecurringStartup ");
    }

    [Fact]
    public void JobSuffixMatch_IsCaseSensitive()
    {
        var (sources, _, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class ImportJOB : IJob {{JobBody}}
            """);

        errors.Should().BeEmpty();
        sources.Should().ContainSingle().Which.Should().Contain("ISchedulingBuilder AddImportJOBJob(");
    }

    [Fact]
    public void TwoJobsMappingToTheSameName_ReportZASCH011OnBoth_AndGenerateNeither()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class Cleanup : IJob {{JobBody}}
            [Job]
            public sealed class CleanupJob : IJob {{JobBody}}
            """);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "ZASCH011");
        diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Error);
        diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Should().OnlyContain(m => m.Contains("AddCleanupJob", StringComparison.Ordinal));
        sources.Should().BeEmpty();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void SameNameInDifferentNamespaces_IsNotACollision()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace A { [Job] public sealed class Cleanup : IJob {{JobBody}} }
            namespace B { [Job] public sealed class CleanupJob : IJob {{JobBody}} }
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        sources.Should().HaveCount(2);
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
        source.Should().NotContain("TypeExecutor : global::ZeroAlloc.Scheduling.IJobTypeExecutor"); // no direct executor class
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
        source.Should().NotContain("TypeExecutor : global::ZeroAlloc.Scheduling.IJobTypeExecutor"); // no direct executor
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
