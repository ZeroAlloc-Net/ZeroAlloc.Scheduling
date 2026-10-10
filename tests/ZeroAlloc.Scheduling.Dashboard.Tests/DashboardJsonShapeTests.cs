using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>Pins the exact body each dashboard endpoint writes, so serialization changes cannot drift it.</summary>
public sealed class DashboardJsonShapeTests
{
    private static WebApplicationFactory<Program> CreateFactory<TStore>()
        where TStore : class, IJobStore, new()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobStore>();
            services.AddSingleton<IJobStore>(new TStore());
        }));

    private static async Task AssertBodyAsync<TStore>(string path, string expected)
        where TStore : class, IJobStore, new()
    {
        using var factory = CreateFactory<TStore>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString().Should().Be("application/json; charset=utf-8");
        (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Be(expected);
    }

    [Fact]
    public Task Summary_Writes_The_Counts() => AssertBodyAsync<FixedDashboardStore>("/jobs/api/summary", SampleJobs.SummaryJson);

    [Fact]
    public Task Pending_Writes_The_Pending_Entry()
        => AssertBodyAsync<FixedDashboardStore>("/jobs/api/pending", "[" + SampleJobs.PendingJson + "]");

    [Fact]
    public Task Running_Writes_The_Running_Entry()
        => AssertBodyAsync<FixedDashboardStore>("/jobs/api/running", "[" + SampleJobs.RunningJson + "]");

    [Fact]
    public Task Failed_Writes_The_Dead_Letter_And_Failed_Entries()
        => AssertBodyAsync<FixedDashboardStore>(
            "/jobs/api/failed", "[" + SampleJobs.DeadLetterJson + "," + SampleJobs.FailedJson + "]");

    [Fact]
    public Task Succeeded_Writes_The_Succeeded_Entry_With_Every_Field()
        => AssertBodyAsync<FixedDashboardStore>("/jobs/api/succeeded", "[" + SampleJobs.SucceededJson + "]");

    [Fact]
    public Task Recurring_Writes_The_Entry_With_A_Cron_Expression()
        => AssertBodyAsync<FixedDashboardStore>("/jobs/api/recurring", "[" + SampleJobs.SucceededJson + "]");

    [Theory]
    [InlineData("/jobs/api/pending")]
    [InlineData("/jobs/api/running")]
    [InlineData("/jobs/api/failed")]
    [InlineData("/jobs/api/succeeded")]
    [InlineData("/jobs/api/recurring")]
    public Task A_Store_Without_Dashboard_Support_Writes_An_Empty_Array(string path)
        => AssertBodyAsync<PlainJobStore>(path, "[]");

    [Fact]
    public Task A_Store_Without_Dashboard_Support_Writes_A_Zero_Summary()
        => AssertBodyAsync<PlainJobStore>(
            "/jobs/api/summary", "{\"pending\":0,\"running\":0,\"succeeded\":0,\"failed\":0,\"deadLetter\":0}");

    [Fact]
    public async Task Requeue_And_Delete_Write_An_Empty_200_Body()
    {
        using var factory = CreateFactory<FixedDashboardStore>();
        using var client = factory.CreateClient();

        var requeue = await client.PostAsync(new Uri($"/jobs/api/{SampleJobs.PendingId}/requeue", UriKind.Relative), null);
        var delete = await client.DeleteAsync(new Uri($"/jobs/api/{SampleJobs.PendingId}", UriKind.Relative));

        requeue.StatusCode.Should().Be(HttpStatusCode.OK);
        (await requeue.Content.ReadAsStringAsync()).Should().BeEmpty();
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await delete.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Index_Page_Is_Served_As_Html_On_Both_Routes()
    {
        using var factory = CreateFactory<FixedDashboardStore>();
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/jobs/", "/jobs/index.html" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        }
    }
}
