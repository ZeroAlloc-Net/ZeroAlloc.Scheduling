using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Dashboard.Tests;

public sealed class DashboardApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DashboardApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

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
        var r = await _client.PostAsync($"/jobs/api/{JobId.New()}/requeue", null);
        ((int)r.StatusCode).Should().BeInRange(200, 299);
    }

    [Fact]
    public async Task DeleteNonExistentJob_Returns2xx()
    {
        // The endpoint is idempotent: missing IDs are silently ignored and 200 OK is returned.
        var r = await _client.DeleteAsync($"/jobs/api/{JobId.New()}");
        ((int)r.StatusCode).Should().BeInRange(200, 299);
    }

    [Fact]
    public async Task DashboardRoute_BindsUlidStringToJobId()
    {
        // Reach into the same singleton InMemoryJobStore that the WebApplicationFactory built.
        var store = (InMemoryJobStore)_factory.Services.GetRequiredService<IJobStore>();

        // Seed a dead-lettered job so that requeue has a legal FSM transition (DeadLetter -> Pending).
        await store.EnqueueAsync("BindingProbe", [], DateTimeOffset.UtcNow, 1, null, CancellationToken.None);
        var running = await store.FetchPendingAsync(1, CancellationToken.None);
        var id = running[0].Id;
        await store.DeadLetterAsync(id, "seed", CancellationToken.None);

        var ulidString = id.ToString(); // ULID base32, 26 chars
        ulidString.Should().HaveLength(26);

        var response = await _client.PostAsync($"/jobs/api/{ulidString}/requeue", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // If the route bound the ULID into the same JobId value, the FSM transitioned to Pending.
        var requeued = store.AllEntries.Single(e => e.Id == id);
        requeued.Status.Should().Be(JobStatus.Pending);
    }
}
