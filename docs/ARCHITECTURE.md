This document explains the architectural decisions behind `ServerMetricsApi`
and the concepts they rest on. It's written for someone who knows C# but
hasn't necessarily built an ASP.NET Core application before — each section
explains the general concept first, then shows how it's applied in this
specific codebase.

For how to install and run the project, see [SETUP.md](SETUP.md). For how data moves between MySQL and C# objects, see
[DATA-ACCESS.md](DATA-ACCESS.md).

## Table of contents

- [Configuring a .NET WebApplication](#configuring-a-net-webapplication)
- [Dependency Injection and Inversion of Control](#dependency-injection-and-inversion-of-control)
- [Separation of Concerns](#separation-of-concerns)
- [Domain Models vs. Data Transfer Objects](#domain-models-vs-data-transfer-objects)

---

## Configuring a .NET WebApplication

### The builder pattern

Every ASP.NET Core application starts the same way, in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
```

`WebApplication.CreateBuilder(args)` is a static method — called directly on
the `WebApplication` class, not on an instance of it — that returns a
`WebApplicationBuilder`. Think of it as a construction kit: you configure
everything the application needs *before* it actually starts running, and
only once configuration is complete do you build the real, runnable
application from it.

`args` are the command-line arguments the process was started with. ASP.NET
Core uses them, among other things, to let configuration values be
overridden from the command line without touching any file.

### Configuration sources: `builder.Configuration`

```csharp
string connectionString = builder.Configuration.GetConnectionString("MetricsDb")
    ?? throw new InvalidOperationException("Connection string 'MetricsDb' fehlt in appsettings.json");
```

`builder.Configuration` is not just a reader for `appsettings.json` — it's a
layered configuration system that automatically merges several sources, in a
defined priority order (later sources override earlier ones):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g. `appsettings.Production.json`)
3. Environment variables
4. Command-line arguments

You never had to write code that says "now read the JSON file" — this
happens automatically as part of `CreateBuilder`. `GetConnectionString("MetricsDb")`
is a convenience method that looks specifically under the `ConnectionStrings`
section for a key named `MetricsDb` — it's shorthand for
`builder.Configuration["ConnectionStrings:MetricsDb"]`.

This layering matters in production: instead of putting a real password in
`appsettings.json` (and risking it ending up in version control), you can
set an environment variable `ConnectionStrings__MetricsDb` on the server,
and it silently takes precedence — no code change needed. (Note the double
underscore `__` — that's how ASP.NET Core represents the `:` nesting from
JSON when environment variables can't contain colons.)

### Registering services: `builder.Services`

```csharp
builder.Services.AddSingleton(new MetricsRepository(connectionString));
```

`builder.Services` is the Dependency Injection container itself — see the
[next section](#dependency-injection-and-inversion-of-control) for a full
explanation of what that means and why it matters.

### CORS configuration

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

Browsers enforce a security rule called the **same-origin policy**:
JavaScript running on one origin (e.g. a dashboard served from
`http://localhost:3000`) is blocked by default from making requests to a
different origin (e.g. this API at `http://localhost:5000`) unless the
server explicitly opts in. CORS (Cross-Origin Resource Sharing) is the
mechanism for that opt-in. `AllowAnyOrigin()` is the most permissive setting
— fine during development, but before exposing this API publicly, it should
be narrowed to the actual origin of the dashboard that's allowed to call it.

### Building and running

```csharp
var app = builder.Build();
```

`Build()` takes everything configured on `builder` and produces the actual,
runnable `WebApplication` — the `app` object. Nothing has started listening
for requests yet at this point; `app` is fully configured but still idle.

```csharp
app.UseCors();

app.MapGet("/api/status", async (MetricsRepository repo) => { ... });
```

Each `app.MapGet(path, handler)` call registers a route — a URL pattern
combined with the code that should run when a request matches it. This is
the **Minimal API** style (introduced in .NET 6), as opposed to the older
**Controller-based** style, where routes are declared as methods inside
`[ApiController]` classes decorated with attributes like `[HttpGet]`. Both
approaches ultimately do the same job; Minimal APIs were chosen here because
this project only has a handful of endpoints, and the reduced boilerplate
keeps the whole routing table readable in a single file. Controllers become
more attractive once an application has dozens of related endpoints that
benefit from being grouped into classes.

```csharp
app.Run();
```

This starts Kestrel (ASP.NET Core's built-in web server) and blocks,
listening for incoming HTTP requests until the process is stopped. This is
the line responsible for the `Now listening on: http://localhost:5000`
message you see when running `dotnet run`.

---

## Dependency Injection and Inversion of Control

### The general principle

**Inversion of Control (IoC)** is a design principle: instead of a piece of
code actively fetching or constructing the things it depends on, those
dependencies are handed to it from the outside. Control over *how* a
dependency is obtained is inverted — moved from the consumer to something
external.

**Dependency Injection (DI)** is the specific technique most commonly used
to achieve IoC: dependencies are passed in — "injected" — usually through a
class's constructor.

### Why this matters, illustrated with our own code

Compare two versions of `MetricsRepository`. Without DI, a class might fetch
its own configuration:

```csharp
public class MetricsRepository
{
    private readonly string _connectionString;

    public MetricsRepository()
    {
        // The class decides for itself where configuration comes from.
        _connectionString = File.ReadAllText("appsettings.json"); // hardcoded!
    }
}
```

This tightly couples `MetricsRepository` to one specific configuration
source. Testing it with a different database, or loading configuration a
different way, means changing the class itself.

The actual code instead does this:

```csharp
public class MetricsRepository
{
    private readonly string _connectionString;

    public MetricsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
}
```

`MetricsRepository` has no idea where `connectionString` came from — a JSON
file, an environment variable, or a value hardcoded in a unit test. It just
receives a string. This is **constructor injection**, the most common form
of DI.

### Who does the injecting?

```csharp
builder.Services.AddSingleton(new MetricsRepository(connectionString));
```

This line does two things: it constructs one instance of `MetricsRepository`
(handing it the real connection string), and registers that instance with
the DI container so the framework knows to hand it out whenever a
`MetricsRepository` is requested elsewhere.

```csharp
app.MapGet("/api/status", async (MetricsRepository repo) => { ... });
```

Here, `MetricsRepository repo` looks like an ordinary method parameter, but
it's DI in action: ASP.NET Core inspects the handler's parameters, recognizes
that a `MetricsRepository` is needed, looks it up in the container, and
passes in the registered instance automatically. No code anywhere calls
`new MetricsRepository(...)` at the call site — the container does that
wiring.

### Service lifetimes

`AddSingleton` is one of three lifetime options the container supports:

| Lifetime | Instance created | Used here? |
|---|---|---|
| `AddSingleton` | Once, reused for the entire application lifetime | Yes — `MetricsRepository` |
| `AddScoped` | Once per HTTP request, shared within that request | Not currently needed |
| `AddTransient` | A new instance every time it's requested | Not currently needed |

`MetricsRepository` is registered as a singleton because it's stateless — it
holds only an immutable connection string and creates a fresh
`MySqlConnection` on every method call (see
[DATA-ACCESS.md](DATA-ACCESS.md)). There's no shared mutable state that could
cause problems if multiple requests use the same instance concurrently. If a
future service needed to track something per-request (e.g. the identity of
the caller), `AddScoped` would be the safer choice.

### Why this is worth caring about

- **Swappability**: a `FakeMetricsRepository` that returns hardcoded test
  data could be registered instead of the real one for automated tests,
  without touching any endpoint code.
- **Single Responsibility**: `MetricsRepository` only worries about SQL, not
  about where configuration comes from.
- **This is not project-specific magic.** `Microsoft.Extensions.DependencyInjection`
  ships as part of the ASP.NET Core SDK — this container is a built-in
  framework feature, not custom code written for this project. The same
  pattern applies to any service registered — logging, HTTP clients,
  caching — at any scale of application.

---

## Separation of Concerns

**Separation of Concerns** is the principle that each part of a system
should be responsible for exactly one thing, and know as little as possible
about how other parts do their job. This project's file layout is a direct
application of it:

```
ServerMetricsApi/
├── Program.cs           → composition root + routing
├── Models/               → shape of data
│   ├── MemoryLogEntry.cs
│   ├── ServerEvent.cs
│   ├── StatusSummary.cs
│   └── AnalysisResult.cs
└── Data/
    └── MetricsRepository.cs   → persistence logic
```

**`Program.cs`** is the *composition root* — the one place where all the
pieces get wired together (DI registrations, middleware, routes). It knows
*that* a `MetricsRepository` exists and *that* it can fetch data, but it has
no idea *how* that data is fetched — no SQL appears anywhere in this file.

**`MetricsRepository.cs`** is the only file that knows SQL exists. Every
query lives here, bundled in one place instead of scattered across endpoint
handlers. If the database engine changed — say, from MySQL to PostgreSQL —
only this file and the connection setup in `Program.cs` would need to
change. The endpoint definitions and the models would be untouched.

**`Models/`** contains plain data containers with no logic at all (see the
next section for the distinction between the two kinds of models used
here).

### Why this is worth the extra files

A single-file version of this project — all SQL, all routing, all models
crammed into `Program.cs` — would work identically for a project this small.
The benefit of separation only shows up as the project grows:

- Adding a new data source (e.g. a second database) means adding a new
  repository class, not editing an existing one.
- Testing `MetricsRepository` in isolation, without spinning up the whole
  web server, becomes straightforward.
- Anyone reading `Program.cs` gets a table of contents for the whole API —
  what endpoints exist — without needing to read SQL to find it.

---

## Domain Models vs. Data Transfer Objects

Both are just C# classes with properties and no real logic — but they exist
for different reasons, and conflating them is a common source of design
problems as an API grows.

### Domain models: mirror the data source

`MemoryLogEntry` and `ServerEvent` are domain models — each property
corresponds directly to a column in an existing database table
(`memory_log` and `server_events` respectively):

```csharp
public class MemoryLogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int TotalMb { get; set; }
    // ... one property per column
}
```

Their job is to represent *what exists in storage*, as faithfully as
possible. Dapper relies on this direct correspondence — the property names
(in PascalCase) matching the SQL column aliases (see
[DATA-ACCESS.md](DATA-ACCESS.md)) is exactly what makes the automatic
mapping work.

### DTOs: shaped for a specific consumer

`StatusSummary` is different — it does **not** correspond to any table:

```csharp
public class StatusSummary
{
    public bool IsOnline { get; set; }
    public DateTime LastMeasurement { get; set; }
    public int WorldserverRssMb { get; set; }
    public int AuthserverRssMb { get; set; }
    public int CharactersOnline { get; set; }
    public int WorldserverUptimeSec { get; set; }
}
```

`IsOnline` isn't a column anywhere — it's *computed* in
`MetricsRepository.GetCurrentStatusAsync()` from comparing the latest
timestamp against the current time. `StatusSummary` exists purely to define
what the `/api/status` endpoint's JSON response looks like — a **Data
Transfer Object**, shaped around what a specific API consumer needs, not
around what a table happens to contain.

### Why the distinction matters

If `/api/status` simply returned a raw `MemoryLogEntry`, two problems would
appear as the project evolves:

1. **The database schema and the API contract become the same thing.**
   Adding an internal-only column to `memory_log` (say, a debug flag nobody
   outside the analysis tool should see) would immediately leak it into the
   public API response, because there'd be no separate object shaping what
   actually gets exposed.
2. **Computed fields have nowhere to live.** `IsOnline` doesn't exist in the
   database at all — it's an interpretation of the data, decided in
   application code. A domain model, being a direct table mirror, has no
   natural place for it.

`AnalysisResult` sits in between the two categories: it does mirror a real
table (`analysis_results`), so structurally it's a domain model — but its
columns (`MetricName`, `Value`, `Unit`) were deliberately designed to be
generic, so the *same* domain model can represent many different kinds of
analysis output without needing a new DTO (or a new table) for each one.

**Rule of thumb applied in this codebase:** if a class's shape is dictated
by "what does this SQL query return", it's a domain model, living naturally
close to `MetricsRepository`. If its shape is dictated by "what should this
specific endpoint's response contain", it's a DTO, and its fields may draw
from multiple domain models, computed values, or both.

---
