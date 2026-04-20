using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.Dashboard;
using ZeroAlloc.Scheduling.InMemory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddScheduling();
builder.Services.AddSchedulingInMemory();
builder.Services.AddRouting();

var app = builder.Build();
app.UseRouting();
app.MapJobsDashboard("/jobs");
app.Run();

public partial class Program { }
