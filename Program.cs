using ServerMetricsApi.Data;

var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("MetricsDb")
    ?? throw new InvalidOperationException("Connection string 'MetricsDb' fehlt in appsettings.json");

builder.Services.AddSingleton(new MetricsRepository(connectionString));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors();

app.MapGet("/api/status", async (MetricsRepository repo) =>
{
    var status = await repo.GetCurrentStatusAsync();
    return status is not null ? Results.Ok(status) : Results.NotFound();
});

app.MapGet("/api/metrics", async (MetricsRepository repo, int hours = 24) =>
{
    var metrics = await repo.GetMetricsAsync(hours);
    return Results.Ok(metrics);
});

app.MapGet("/api/events", async (MetricsRepository repo, int days = 7) =>
{
    var events = await repo.GetEventsAsync(days);
    return Results.Ok(events);
});

app.Run();
