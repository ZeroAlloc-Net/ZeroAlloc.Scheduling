---
id: dashboard
title: Dashboard
slug: /docs/dashboard
description: Embedded HTML dashboard and Blazor component for monitoring and managing background jobs.
sidebar_position: 4
---

# Dashboard

ZeroAlloc.Scheduling ships two dashboard options: a self-contained embedded HTML dashboard served via Minimal API, and a Blazor component for integration into existing Blazor applications.

## Embedded HTML Dashboard

One line wires up the full dashboard:

```bash
dotnet add package ZeroAlloc.Scheduling.Dashboard
```

```csharp
app.MapJobsDashboard("/jobs");
```

Open `/jobs/` in a browser to see:

- **Summary cards** — counts for pending, running, succeeded, failed, and dead-lettered jobs
- **Jobs table** — ID, type, status, attempts, scheduled time, last error, and actions
- **Auto-refresh** — the page polls all API endpoints every 5 seconds
- **Requeue** — re-enqueue a dead-lettered job with one click
- **Delete** — remove any job from the store

The dashboard is served from an embedded resource (no static file middleware required). It calls the following API endpoints automatically:

| Endpoint | Description |
|----------|-------------|
| `GET /jobs/api/summary` | Job counts by status |
| `GET /jobs/api/pending` | Pending jobs (page 1, 50 per page) |
| `GET /jobs/api/running` | Currently running jobs |
| `GET /jobs/api/failed` | Failed jobs awaiting retry |
| `GET /jobs/api/succeeded` | Recently succeeded jobs |
| `POST /jobs/api/{id}/requeue` | Re-enqueue a dead-lettered job |
| `DELETE /jobs/api/{id}` | Delete a job |

### Authentication

`MapJobsDashboard` returns an `IEndpointConventionBuilder`, so standard ASP.NET Core authorization policies apply:

```csharp
app.MapJobsDashboard("/jobs").RequireAuthorization("AdminOnly");
```

## Blazor Component

For Blazor Server or WASM applications that want to embed the dashboard inside their own layout:

```bash
dotnet add package ZeroAlloc.Scheduling.Dashboard.Blazor
```

```csharp
// Register the HTTP client pointing at your dashboard API base URL
services.AddJobsDashboardBlazor(new Uri("https://myapp.example.com/jobs/"));
```

```razor
@* In any .razor page or layout *@
@using ZeroAlloc.Scheduling.Dashboard.Blazor

<JobsDashboard />
```

The component uses `JobsDashboardClient` internally, which calls the same API endpoints as the HTML dashboard. State refreshes automatically every 5 seconds via `System.Threading.Timer`.
