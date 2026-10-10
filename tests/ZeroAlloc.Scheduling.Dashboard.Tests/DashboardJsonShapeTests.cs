using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling.InMemory;

namespace ZeroAlloc.Scheduling.Dashboard.Tests;

/// <summary>Pins the raw JSON each dashboard endpoint writes, so serialization changes cannot drift it.</summary>
public sealed class DashboardJsonShapeTests : IDisposable
{
    private static readonly string[] EntryFields =
    [
        "id", "typeName", "payload", "status", "attempts", "maxAttempts", "scheduledAt",
        "startedAt", "completedAt", "nextRunAt", "cronExpression", "error",
    ];

    // A factory per test: the in-memory store is a singleton, and the tests move jobs between states.
    private readonly WebApplicationFactory<Program> _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.Clone();
    }

    private async Task<(InMemoryJobStore Store, JobId Id)> SeedPendingAsync(string typeName)
    {
        var store = (InMemoryJobStore)_factory.Services.GetRequiredService<IJobStore>();
        await store.EnqueueAsync(typeName, [1, 2, 3], DateTimeOffset.UtcNow, 3, null, CancellationToken.None).ConfigureAwait(false);
        return (store, store.AllEntries.Single(e => string.Equals(e.TypeName, typeName, StringComparison.Ordinal)).Id);
    }

    private static JsonElement FindEntry(JsonElement array, string typeName)
        => array.EnumerateArray().Single(e => string.Equals(e.GetProperty("typeName").GetString(), typeName, StringComparison.Ordinal));

    [Fact]
    public async Task Summary_Writes_The_Counts_In_CamelCase()
    {
        var json = await GetJsonAsync("/jobs/api/summary");

        foreach (var field in new[] { "pending", "running", "succeeded", "failed", "deadLetter" })
            json.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Number, field);
    }

    [Fact]
    public async Task Pending_Writes_Entries_In_CamelCase()
    {
        var (_, id) = await SeedPendingAsync("ShapePending");

        var json = await GetJsonAsync("/jobs/api/pending");

        json.ValueKind.Should().Be(JsonValueKind.Array);
        var entry = FindEntry(json, "ShapePending");
        foreach (var field in EntryFields)
            entry.TryGetProperty(field, out _).Should().BeTrue(field);
        entry.GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
        entry.GetProperty("id").GetString().Should().Be(id.ToString()).And.HaveLength(26);
        entry.GetProperty("payload").GetString().Should().Be("AQID", "byte arrays are base64");
        entry.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String, "JobStatus is written as its name");
        entry.GetProperty("status").GetString().Should().Be("Pending");
        entry.GetProperty("maxAttempts").GetInt32().Should().Be(3);
        entry.GetProperty("scheduledAt").ValueKind.Should().Be(JsonValueKind.String);
        entry.GetProperty("startedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Running_Writes_Entries_In_CamelCase()
    {
        var (store, id) = await SeedPendingAsync("ShapeRunning");
        await store.FetchPendingAsync(100, CancellationToken.None);

        var json = await GetJsonAsync("/jobs/api/running");

        json.ValueKind.Should().Be(JsonValueKind.Array);
        var entry = FindEntry(json, "ShapeRunning");
        entry.GetProperty("id").GetString().Should().Be(id.ToString());
        entry.GetProperty("status").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task Failed_Writes_Dead_Lettered_Entries_With_The_Error()
    {
        var (store, id) = await SeedPendingAsync("ShapeFailed");
        await store.FetchPendingAsync(100, CancellationToken.None);
        await store.DeadLetterAsync(id, "boom", CancellationToken.None);

        var json = await GetJsonAsync("/jobs/api/failed");

        var entry = FindEntry(json, "ShapeFailed");
        entry.GetProperty("error").GetString().Should().Be("boom");
        entry.GetProperty("status").GetString().Should().Be("DeadLetter");
    }

    [Fact]
    public async Task Succeeded_Writes_An_Array()
    {
        var json = await GetJsonAsync("/jobs/api/succeeded");

        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Recurring_Writes_Entries_With_The_Cron_Expression()
    {
        var store = (InMemoryJobStore)_factory.Services.GetRequiredService<IJobStore>();
        await store.UpsertRecurringAsync("ShapeRecurring", [1, 2, 3], DateTimeOffset.UtcNow, "0 * * * *", 3, CancellationToken.None);

        var json = await GetJsonAsync("/jobs/api/recurring");

        json.ValueKind.Should().Be(JsonValueKind.Array);
        var entry = FindEntry(json, "ShapeRecurring");
        entry.GetProperty("cronExpression").GetString().Should().Be("0 * * * *");
    }

    [Fact]
    public async Task Requeue_And_Delete_Write_An_Empty_200_Body()
    {
        using var client = _factory.CreateClient();
        var requeue = await client.PostAsync(new Uri($"/jobs/api/{JobId.New()}/requeue", UriKind.Relative), null);
        var delete = await client.DeleteAsync(new Uri($"/jobs/api/{JobId.New()}", UriKind.Relative));

        requeue.StatusCode.Should().Be(HttpStatusCode.OK);
        (await requeue.Content.ReadAsStringAsync()).Should().BeEmpty();
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await delete.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Index_Page_Is_Served_As_Html_On_Both_Routes()
    {
        using var client = _factory.CreateClient();
        foreach (var path in new[] { "/jobs/", "/jobs/index.html" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        }
    }
}
