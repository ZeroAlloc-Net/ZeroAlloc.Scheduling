using System.Net;
using System.Text;
using ZeroAlloc.Scheduling.Dashboard.Blazor;

namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>Each client method must still read the JSON the dashboard API writes.</summary>
public sealed class BlazorClientTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static readonly JobId SampleId = JobId.New();

    private static readonly string EntryArray =
        "[{\"id\":\"" + SampleId + "\",\"typeName\":\"Sample\",\"payload\":\"AQID\",\"status\":\"Succeeded\",\"attempts\":1,"
        + "\"maxAttempts\":3,\"scheduledAt\":\"2026-01-02T03:04:05+00:00\",\"startedAt\":null,\"completedAt\":null,"
        + "\"nextRunAt\":\"2026-01-03T03:04:05+00:00\",\"cronExpression\":\"0 * * * *\",\"error\":\"boom\"}]";

    private static (JobsDashboardClient Client, StubHandler Handler) Create(string body)
    {
        var handler = new StubHandler(body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://dash.test/jobs/api/") };
        return (new JobsDashboardClient(http), handler);
    }

    private static void AssertEntries(IReadOnlyList<JobEntry>? entries)
    {
        entries.Should().NotBeNull();
        var entry = entries!.Should().ContainSingle().Subject;
        entry.Id.Should().Be(SampleId);
        entry.TypeName.Should().Be("Sample");
        entry.Payload.Should().Equal(1, 2, 3);
        entry.Status.Should().Be(JobStatus.Succeeded);
        entry.Attempts.Should().Be(1);
        entry.MaxAttempts.Should().Be(3);
        entry.ScheduledAt.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        entry.StartedAt.Should().BeNull();
        entry.NextRunAt.Should().Be(new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero));
        entry.CronExpression.Should().Be("0 * * * *");
        entry.Error.Should().Be("boom");
    }

    [Fact]
    public async Task GetSummaryAsync_Reads_The_Counts()
    {
        var (client, handler) = Create("{\"pending\":1,\"running\":2,\"succeeded\":3,\"failed\":4,\"deadLetter\":5}");

        var summary = await client.GetSummaryAsync();

        summary.Should().Be(new JobSummary(1, 2, 3, 4, 5));
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/summary"));
    }

    [Fact]
    public async Task GetPendingAsync_Reads_Entries()
    {
        var (client, handler) = Create(EntryArray);
        AssertEntries(await client.GetPendingAsync());
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/pending"));
    }

    [Fact]
    public async Task GetRunningAsync_Reads_Entries()
    {
        var (client, handler) = Create(EntryArray);
        AssertEntries(await client.GetRunningAsync());
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/running"));
    }

    [Fact]
    public async Task GetFailedAsync_Reads_Entries()
    {
        var (client, handler) = Create(EntryArray);
        AssertEntries(await client.GetFailedAsync());
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/failed"));
    }

    [Fact]
    public async Task GetSucceededAsync_Reads_Entries()
    {
        var (client, handler) = Create(EntryArray);
        AssertEntries(await client.GetSucceededAsync());
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/succeeded"));
    }

    [Fact]
    public async Task GetPendingAsync_Reads_An_Empty_Array()
    {
        var (client, _) = Create("[]");
        (await client.GetPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RequeueAsync_And_DeleteAsync_Send_The_Expected_Requests()
    {
        var (client, handler) = Create("");

        await client.RequeueAsync(SampleId);
        await client.DeleteAsync(SampleId);

        handler.Requests.Should().Equal(
            (HttpMethod.Post, $"/jobs/api/{SampleId}/requeue"),
            (HttpMethod.Delete, $"/jobs/api/{SampleId}"));
    }
}
