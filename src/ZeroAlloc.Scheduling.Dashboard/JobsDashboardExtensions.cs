using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ZeroAlloc.Scheduling.Dashboard;

public static class JobsDashboardExtensions
{
    public static IEndpointConventionBuilder MapJobsDashboard(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        Action<IEndpointConventionBuilder>? configure = null)
    {
        var group = endpoints.MapGroup(prefix);
        configure?.Invoke(group);

        MapUiRoutes(group);
        MapApiRoutes(group);

        return group;
    }

    private static void MapUiRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/", ServeUi);
        group.MapGet("/index.html", ServeUi);
    }

    private static void MapApiRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/api/summary", async (IJobStore store, CancellationToken ct) =>
            store is IJobDashboardStore d
                ? Results.Ok(await d.GetSummaryAsync(ct).ConfigureAwait(false))
                : Results.Ok(new JobSummary(0, 0, 0, 0, 0)));

        group.MapGet("/api/pending", (IJobStore s, CancellationToken ct) =>
            QueryStatusAsync(s, ct, JobStatus.Pending));

        group.MapGet("/api/running", (IJobStore s, CancellationToken ct) =>
            QueryStatusAsync(s, ct, JobStatus.Running));

        group.MapGet("/api/failed", (IJobStore s, CancellationToken ct) =>
            QueryStatusAsync(s, ct, JobStatus.Failed, JobStatus.DeadLetter));

        group.MapGet("/api/succeeded", (IJobStore s, CancellationToken ct) =>
            QueryStatusAsync(s, ct, JobStatus.Succeeded));

        group.MapGet("/api/recurring", async (IJobStore s, CancellationToken ct) =>
            s is IJobDashboardStore d
                ? Results.Ok(await d.GetRecurringAsync(ct).ConfigureAwait(false))
                : Results.Ok(Array.Empty<object>()));

        group.MapPost("/api/{id:guid}/requeue", async (Guid id, IJobStore s, CancellationToken ct) =>
        {
            if (s is IJobDashboardStore d) await d.RequeueAsync(id, ct).ConfigureAwait(false);
            return Results.Ok();
        });

        group.MapDelete("/api/{id:guid}", async (Guid id, IJobStore s, CancellationToken ct) =>
        {
            if (s is IJobDashboardStore d) await d.DeleteAsync(id, ct).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    private static async Task<IResult> QueryStatusAsync(IJobStore store, CancellationToken ct, params JobStatus[] statuses)
        => store is IJobDashboardStore d
            ? Results.Ok(await d.QueryByStatusAsync(statuses, ct: ct).ConfigureAwait(false))
            : Results.Ok(Array.Empty<object>());

    private static IResult ServeUi()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        if (resource is null) return Results.NotFound("Dashboard UI not found.");
        var stream = asm.GetManifestResourceStream(resource)!;
        return Results.Stream(stream, "text/html");
    }
}
