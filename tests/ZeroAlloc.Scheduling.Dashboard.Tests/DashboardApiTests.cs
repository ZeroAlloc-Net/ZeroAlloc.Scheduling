using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ZeroAlloc.Scheduling.Dashboard.Tests;

public sealed class DashboardApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DashboardApiTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetSummary_Returns200()
    {
        var r = await _client.GetAsync("/jobs/api/summary");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JobSummary>();
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPending_Returns200()
        => (await _client.GetAsync("/jobs/api/pending")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task DashboardRoot_ServesHtml()
    {
        var r = await _client.GetAsync("/jobs/");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("<html");
    }
}
