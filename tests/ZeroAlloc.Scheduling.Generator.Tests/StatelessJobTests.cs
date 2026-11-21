namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// A job without instance fields carries no state, so the generated code neither reads nor writes
/// a payload for it and needs no <c>IJobSerializer</c>. Every other job keeps the serializer.
/// </summary>
public sealed class StatelessJobTests
{
    private const string Execute =
        "public System.Threading.Tasks.ValueTask ExecuteAsync(JobContext ctx, System.Threading.CancellationToken ct) => default;";

    [Fact]
    public void RecurringJob_WithoutFields_NeedsNoSerializer_AndSeedsAnEmptyPayload()
    {
        var (sources, diagnostics, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job(Every = Every.Hour)]
            internal sealed class PruneJob : IJob
            {
                private static int s_runs;
                private const int Limit = 3;
                public static int Runs => s_runs;
                {{Execute}}
            }
            """);

        diagnostics.Should().BeEmpty();
        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().NotContain("IJobSerializer");
        source.Should().Contain("var payload = global::System.Array.Empty<byte>();");
        source.Should().Contain("private static readonly global::MyApp.PruneJob s_job = new global::MyApp.PruneJob();");
        source.Should().Contain("=> s_job.ExecuteAsync(ctx, ct);");
    }

    [Fact]
    public void OneOffJob_WithoutFields_GetsTheStatelessExecutor()
    {
        var (sources, _, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class PingJob : IJob
            {
                public PingJob() { }
                {{Execute}}
            }
            """);

        errors.Should().BeEmpty();
        sources.Should().ContainSingle().Which.Should().NotContain("IJobSerializer");
    }

    [Theory]
    [InlineData("public string To { get; init; } = \"\";", "")]
    [InlineData("private readonly int _count;", "")]
    [InlineData("", "public PingJob(int count) { }")]
    [InlineData("", "private PingJob() { }")]
    public void Job_WithStateOrWithoutAReachableConstructor_KeepsTheSerializer(string field, string constructor)
    {
        var (sources, _, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            [Job]
            public sealed class PingJob : IJob
            {
                {{field}}
                {{constructor}}
                {{Execute}}
            }
            """);

        errors.Should().BeEmpty();
        sources.Should().ContainSingle().Which.Should().Contain("_serializer.Deserialize<global::MyApp.PingJob>(payload)");
    }

    [Fact]
    public void Job_WhoseBaseTypeHasAField_KeepsTheSerializer()
    {
        var (sources, _, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            namespace MyApp;
            public abstract class JobBase { protected int Count; }
            [Job(Cron = "0 * * * *")]
            public sealed class CountJob : JobBase, IJob
            {
                {{Execute}}
            }
            """);

        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().Contain("_serializer.Deserialize<global::MyApp.CountJob>(payload)");
        source.Should().Contain("var payload = _serializer.Serialize(new global::MyApp.CountJob());");
    }

    [Fact]
    public void RecurringMediatorBridgeJob_WithoutFields_KeepsTheSerializer()
    {
        // The bridge's executor is a library type that deserializes the payload, so the seed
        // must still be serialized.
        var (sources, _, errors) = GeneratorTestHelper.RunAll($$"""
            using ZeroAlloc.Scheduling;
            using ZeroAlloc.Mediator;
            namespace MyApp;
            [Job(Cron = "0 * * * *")]
            public sealed class HourlyReportJob : IJob, IRequest<Unit>
            {
                {{Execute}}
            }
            """);

        errors.Should().BeEmpty();
        var source = sources.Should().ContainSingle().Subject;
        source.Should().Contain("MediatorJobTypeExecutor");
        source.Should().Contain("var payload = _serializer.Serialize(new global::MyApp.HourlyReportJob());");
    }
}
