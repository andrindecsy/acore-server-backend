using ServerMetricsApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Connection String aus appsettings.json lesen und das Repository als
// Singleton registrieren - eine Instanz für die gesamte Laufzeit der App,
// da das Repository selbst keinen veränderlichen Zustand hält (nur den
// unveränderlichen Connection String).
string connectionString = builder.Configuration.GetConnectionString("MetricsDb")
    ?? throw new InvalidOperationException("Connection string 'MetricsDb' fehlt in appsettings.json");

builder.Services.AddSingleton(new MetricsRepository(connectionString));

// CORS: solange das Dashboard (z.B. ein separates React-Projekt) von einer
// anderen Origin (anderer Port/Domain) aus auf die API zugreift, muss das
// explizit erlaubt werden. Für den Start reicht "alles erlauben" - vor
// einem produktiven Einsatz würde man das auf konkrete Origins eingrenzen.
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

// GET /api/status - aktueller Snapshot, für z.B. den Discord-Bot oder eine
// einzelne "Server online?" Anzeige im Dashboard.
app.MapGet("/api/status", async (MetricsRepository repo) =>
{
    var status = await repo.GetCurrentStatusAsync();
    return status is not null ? Results.Ok(status) : Results.NotFound();
});

// GET /api/metrics?hours=24 - Zeitreihe für Graphen. Default 24h, falls
// kein Query-Parameter mitgeschickt wird.
app.MapGet("/api/metrics", async (MetricsRepository repo, int hours = 24) =>
{
    var metrics = await repo.GetMetricsAsync(hours);
    return Results.Ok(metrics);
});

// GET /api/events?days=7 - Liste der letzten Events (Crashes, Neustarts).
app.MapGet("/api/events", async (MetricsRepository repo, int days = 7) =>
{
    var events = await repo.GetEventsAsync(days);
    return Results.Ok(events);
});

app.Run();
