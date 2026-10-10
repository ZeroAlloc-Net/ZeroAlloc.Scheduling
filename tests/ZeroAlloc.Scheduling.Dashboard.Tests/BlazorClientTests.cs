using System.Net;
using System.Text;
using ZeroAlloc.Scheduling.Dashboard.Blazor;

namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>
/// Each client method is fed the exact JSON the dashboard API writes, and every field of the
/// deserialized result is asserted.
/// </summary>
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

    private static (JobsDashboardClient Client, StubHandler Handler) Create(string body)
    {
        var handler = new StubHandler(body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://dash.test/jobs/api/") };
        return (new JobsDashboardClient(http), handler);
    }

    private static void AssertSame(JobEntry actual, JobEntry expected)
    {
        actual.Id.Should().Be(expected.Id);
        actual.TypeName.Should().Be(expected.TypeName);
        actual.Payload.Should().Equal(expected.Payload);
        actual.Status.Should().Be(expected.Status);
        actual.Attempts.Should().Be(expected.Attempts);
        actual.MaxAttempts.Should().Be(expected.MaxAttempts);
        actual.ScheduledAt.Should().Be(expected.ScheduledAt);
        actual.StartedAt.Should().Be(expected.StartedAt);
        actual.CompletedAt.Should().Be(expected.CompletedAt);
        actual.NextRunAt.Should().Be(expected.NextRunAt);
        actual.CronExpression.Should().Be(expected.CronExpression);
        actual.Error.Should().Be(expected.Error);
    }

    private static void AssertSame(IReadOnlyList<JobEntry>? actual, params JobEntry[] expected)
    {
        actual.Should().NotBeNull();
        actual!.Should().HaveCount(expected.Length);
        for (var i = 0; i < expected.Length; i++)
            AssertSame(actual[i], expected[i]);
    }

    [Fact]
    public async Task GetSummaryAsync_Reads_Every_Count()
    {
        var (client, handler) = Create(SampleJobs.SummaryJson);

        var summary = await client.GetSummaryAsync();

        summary.Should().NotBeNull();
        summary!.Pending.Should().Be(1);
        summary.Running.Should().Be(2);
        summary.Succeeded.Should().Be(3);
        summary.Failed.Should().Be(4);
        summary.DeadLetter.Should().Be(5);
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/summary"));
    }

    [Fact]
    public async Task GetPendingAsync_Reads_Every_Field_Of_An_Entry_With_Nulls()
    {
        var (client, handler) = Create("[" + SampleJobs.PendingJson + "]");

        AssertSame(await client.GetPendingAsync(), SampleJobs.Pending);
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/pending"));
    }

    [Fact]
    public async Task GetRunningAsync_Reads_Every_Field_Of_An_Entry()
    {
        var (client, handler) = Create("[" + SampleJobs.RunningJson + "]");

        AssertSame(await client.GetRunningAsync(), SampleJobs.Running);
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/running"));
    }

    [Fact]
    public async Task GetFailedAsync_Reads_Every_Field_Of_Two_Entries()
    {
        var (client, handler) = Create("[" + SampleJobs.DeadLetterJson + "," + SampleJobs.FailedJson + "]");

        AssertSame(await client.GetFailedAsync(), SampleJobs.DeadLetter, SampleJobs.Failed);
        handler.Requests.Should().Equal((HttpMethod.Get, "/jobs/api/failed"));
    }

    [Fact]
    public async Task GetSucceededAsync_Reads_Every_Field_Of_An_Entry_With_All_Dates()
    {
        var (client, handler) = Create("[" + SampleJobs.SucceededJson + "]");

        AssertSame(await client.GetSucceededAsync(), SampleJobs.Succeeded);
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

        await client.RequeueAsync(SampleJobs.Pending.Id);
        await client.DeleteAsync(SampleJobs.Pending.Id);

        handler.Requests.Should().Equal(
            (HttpMethod.Post, $"/jobs/api/{SampleJobs.PendingId}/requeue"),
            (HttpMethod.Delete, $"/jobs/api/{SampleJobs.PendingId}"));
    }
}
