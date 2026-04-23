using ZeroAlloc.Scheduling;
using ZeroAlloc.Scheduling.Dashboard;
using ZeroAlloc.Scheduling.InMemory;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScheduling();
builder.Services.AddSchedulingInMemory();

var app = builder.Build();
app.MapJobsDashboard("/jobs");
app.Run();
