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

    [Fact]
    public async Task GetRunning_Returns200()
        => (await _client.GetAsync("/jobs/api/running")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetFailed_Returns200()
        => (await _client.GetAsync("/jobs/api/failed")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetSucceeded_Returns200()
        => (await _client.GetAsync("/jobs/api/succeeded")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetRecurring_Returns200()
        => (await _client.GetAsync("/jobs/api/recurring")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task RequeueNonExistentJob_Returns2xx()
    {
        // The endpoint is idempotent: missing IDs are silently ignored and 200 OK is returned.
        var r = await _client.PostAsync($"/jobs/api/{Guid.NewGuid()}/requeue", null);
        ((int)r.StatusCode).Should().BeInRange(200, 299);
    }

    [Fact]
    public async Task DeleteNonExistentJob_Returns2xx()
    {
        // The endpoint is idempotent: missing IDs are silently ignored and 200 OK is returned.
        var r = await _client.DeleteAsync($"/jobs/api/{Guid.NewGuid()}");
        ((int)r.StatusCode).Should().BeInRange(200, 299);
    }
}
