This document explains how data actually moves between MySQL and C# objects
in this project: the asynchronous programming model that makes database
calls efficient, the low-level driver that speaks MySQL's wire protocol,
and Dapper, the library that maps raw query results onto C# classes.

For the broader structural decisions (why the code is organized the way it
is), see [ARCHITECTURE.md](ARCHITECTURE.md).

## Table of contents

- [Asynchronous programming, briefly](#asynchronous-programming-briefly)
- [MySqlConnector: the database driver](#mysqlconnector-the-database-driver)
- [Dapper: mapping rows to objects](#dapper-mapping-rows-to-objects)
- [Tracing one request end to end](#tracing-one-request-end-to-end)

---

## Asynchronous programming, briefly

### The problem it solves

Every method in `MetricsRepository` ultimately waits on a network round-trip
to MySQL. Even a fast local query takes a few milliseconds — an eternity
compared to CPU speed. If a thread simply blocked and waited during that
time, it could do nothing else. On a web server handling many concurrent
requests, having every in-flight request tie up its own thread while idly
waiting on I/O would badly limit how many requests the server can handle at
once.

### `async` and `await`

```csharp
public async Task<StatusSummary?> GetCurrentStatusAsync()
{
    using var connection = GetConnection();
    var latest = await connection.QuerySingleOrDefaultAsync(sql);
    ...
}
```

`await` marks the point where execution can be suspended: "start this
operation, and free up the thread to do other work while waiting for the
result; when the result arrives, resume here." The method containing an
`await` must itself be marked `async` — this is a compiler requirement, not
optional decoration.

Crucially, this is *not* the same as multithreading in the sense of running
code in parallel. A single thread can be juggling many suspended `async`
operations, resuming each one briefly as its result becomes available. The
benefit is throughput under I/O-bound waiting, not raw parallel computation.

### `Task` and `Task<T>`

An `async` method can't return `T` directly, because it isn't necessarily
finished by the time the calling code moves on. Instead, it returns a
`Task<T>` — an object representing a *future* result: a promise that a `T`
will eventually be available, plus the means to wait for it (`await`) or
otherwise react to its completion.

The return types used in `MetricsRepository` reflect exactly what each
method eventually produces:

| Return type | Meaning |
|---|---|
| `Task<StatusSummary?>` | Eventually either a `StatusSummary`, or `null` if no measurement exists yet |
| `Task<IEnumerable<MemoryLogEntry>>` | Eventually a sequence of `MemoryLogEntry` objects |
| `Task<IEnumerable<ServerEvent>>` | Eventually a sequence of `ServerEvent` objects |
| `Task<IEnumerable<AnalysisResult>>` | Eventually a sequence of `AnalysisResult` objects |

`IEnumerable<T>` is a deliberately loose type: "something that can be
iterated over" (with `foreach`, for instance) — a `List<T>`, an array, or
whatever concrete collection type Dapper happens to hand back internally
all satisfy it. Calling code doesn't need to know or care which one it
actually is.

### A note on pitfalls (not present in this code, but worth knowing)

Two mistakes are common enough to flag even though this project avoids
them:

- **`async void`** — should essentially never be used except for event
  handlers. Unlike `async Task`, exceptions thrown inside an `async void`
  method can't be caught by the caller in the normal way, since there's no
  `Task` to observe or await.
- **Forgetting `await`** — calling an async method without `await`ing it
  compiles, but the operation fires off without the calling code ever
  waiting for or observing its result, which usually isn't the intent.

---

## MySqlConnector: the database driver

### What a driver does

A database driver is the library that translates high-level calls in your
language ("open a connection", "run this query") into the actual
byte-level network protocol a specific database speaks. `MySqlConnector` is
the driver for MySQL — it knows how to open a TCP connection, authenticate,
send queries in MySQL's wire format, and parse the binary responses back
into something usable.

### `GetConnection()`

```csharp
private MySqlConnection GetConnection() => new(_connectionString);
```

This is an **expression-bodied method** — shorthand for a single-statement
method body. Written out in full:

```csharp
private MySqlConnection GetConnection()
{
    return new MySqlConnection(_connectionString);
}
```

`new(_connectionString)` is target-typed `new` — since the method's return
type (`MySqlConnection`) is already known, the compiler allows omitting the
type name after `new`. Note that `GetConnection()` is an ordinary method,
not a constructor — it *calls* a constructor (`MySqlConnection`'s) internally
and returns the resulting object. The constructor for `MetricsRepository`
itself is the separate `public MetricsRepository(string connectionString)`
seen earlier in the same file.

### `using var connection = GetConnection();`

Two distinct language features combine in this one line:

- **`var`** is type inference — the compiler already knows, from
  `GetConnection()`'s return type, that `connection` is a `MySqlConnection`,
  so the type doesn't need to be spelled out.
- **`using`** (as a statement modifier here, not an import) guarantees that
  `connection.Dispose()` is called automatically once the enclosing method
  ends — whether it ends normally or via an exception. Without `using`,
  releasing the connection would require manually writing
  `try { ... } finally { connection.Dispose(); }` everywhere. This works
  because `MySqlConnection` implements `IDisposable`, the interface `using`
  requires.

### Connection pooling

Creating a *new* `MySqlConnection` object on every method call looks
wasteful at first glance, but it isn't: `MySqlConnector` maintains a
**connection pool** internally. `Dispose()` doesn't necessarily close the
underlying TCP connection to MySQL — it returns it to a pool of already-open
connections, ready to be reused by the next `GetConnection()` call. The
overhead of `new MySqlConnection(...)` in application code is therefore
mostly just borrowing a connection object from that pool, not renegotiating
a fresh TCP handshake and authentication every time.

### Where Dapper's methods actually come from

`connection.QueryAsync<T>(...)`, `connection.QuerySingleOrDefaultAsync(...)`
and similar calls are **not** methods defined on `MySqlConnection` itself.
They're **extension methods** contributed by Dapper, which attaches them to
any type implementing the generic `IDbConnection` interface —
`MySqlConnection` satisfies that interface, as would the equivalent
connection classes for SQL Server, PostgreSQL, SQLite, and others. This is
why `using Dapper;` needs to appear at the top of `MetricsRepository.cs`:
without that import, those methods wouldn't be visible on `connection` at
all, even though `MySqlConnection` itself hasn't changed.

---

## Dapper: mapping rows to objects

### Micro-ORM, not a full ORM

Dapper describes itself as a "micro-ORM." A full ORM, such as Entity
Framework Core, generates SQL for you from C# expressions and tracks
relationships between objects. Dapper does none of that — **all SQL in this
project is written by hand**, visible directly in every method of
`MetricsRepository`. Dapper's only job is the last step: converting the raw
rows a query returns into instances of a C# class. Less automation, but
full visibility into exactly what SQL runs.

### Typed queries

```csharp
const string sql = """
    SELECT id                      AS Id,
           timestamp                AS Timestamp,
           total_mb                 AS TotalMb,
           ...
    FROM memory_log
    WHERE timestamp >= @Since
    ORDER BY timestamp ASC
    """;

DateTime since = DateTime.UtcNow.AddHours(-hours);
return await connection.QueryAsync<MemoryLogEntry>(sql, new { Since = since });
```

`QueryAsync<MemoryLogEntry>` is generic — it tells Dapper exactly what shape
to map each row into. Dapper matches result columns to properties **by
name**, case-insensitively. This is exactly why every query in this project
aliases columns explicitly with `AS PascalCaseName` — `total_mb AS TotalMb`
— so the SQL's snake_case naming convention lines up with C#'s PascalCase
property naming convention. Without those aliases, Dapper would look for a
property literally named `total_mb`, which doesn't exist on
`MemoryLogEntry`, and that column would simply be left unmapped.

### SQL parameterization

```csharp
new { Since = since }
```

This anonymous object supplies the value for `@Since` in the SQL string.
Dapper sends the query and its parameters to MySQL **separately** — the
value is never concatenated directly into the SQL text. This is what
protects against SQL injection: a malicious or malformed value in `since`
can't be interpreted as SQL syntax, because it's never part of the SQL
string in the first place — it's transmitted as data, alongside the query.

### Dynamic (untyped) queries

`GetCurrentStatusAsync` uses a different call shape:

```csharp
var latest = await connection.QuerySingleOrDefaultAsync(sql);
```

No generic type argument. In this form, Dapper returns a `dynamic` object —
its exact shape isn't known at compile time, only inferred once code
actually tries to access a property on it at runtime.

**This is convenient, but it comes with a real cost.** During development of
this project, the following line caused a runtime exception that a typed
query would have caught much earlier, at compile time:

```csharp
CharactersOnline = latest.CharactersOnline,
```

The database column was `INT`, but the target property was originally
declared as `short`. With a typed `QueryAsync<T>` call, Dapper handles this
kind of numeric widening/narrowing automatically as part of its mapping.
With a `dynamic` result, though, C#'s compiler can't check the assignment
in advance at all — the type mismatch only surfaces as a
`RuntimeBinderException` the first time the code actually runs, since
`dynamic` bypasses compile-time type checking entirely. The eventual fix was
changing every affected property (`MemoryLogEntry.WorldserverThreads`,
`WorldserverFds`, `CharactersOnline`, and `StatusSummary.CharactersOnline`)
to `int`, matching the database's actual column types exactly, removing the
mismatch at its source rather than papering over it with a runtime
conversion.

**Takeaway:** prefer typed `QueryAsync<T>` wherever the target shape is
known ahead of time — it lets the compiler catch type mismatches long
before deployment. Reach for the untyped `dynamic` form only for one-off
results where defining a full model class isn't worth it, and be aware
that doing so trades away compile-time safety for that convenience.

---

## Tracing one request end to end

To tie the whole chain together, here's what happens for a single
`GET /api/status` request, referencing every concept above:

1. Kestrel (started by `app.Run()`) receives the HTTP request and matches it
   to the route registered via `app.MapGet("/api/status", ...)`.
2. The DI container resolves the handler's `MetricsRepository repo`
   parameter, handing over the singleton instance registered in
   `Program.cs`.
3. `repo.GetCurrentStatusAsync()` runs. `GetConnection()` borrows a
   `MySqlConnection` from `MySqlConnector`'s pool.
4. `await connection.QuerySingleOrDefaultAsync(sql)` sends the query over
   that connection and suspends, freeing the thread until MySQL responds —
   this is the `async`/`await` mechanism from the first section.
5. Dapper receives the raw row data back from `MySqlConnector` and exposes
   it as a `dynamic` object.
6. Application code (not SQL) computes `IsOnline` by comparing timestamps,
   and assembles a `StatusSummary` DTO — the domain-model-vs-DTO distinction
   from [ARCHITECTURE.md](ARCHITECTURE.md) in action.
7. The `using` on `connection` releases it back to the pool as the method
   returns.
8. Back in the endpoint handler, `Results.Ok(status)` serializes the
   `StatusSummary` to JSON and writes it as the HTTP response body.

Every layer in this chain only knows about the layer immediately next to
it — Kestrel doesn't know about MySQL, and `MetricsRepository` doesn't know
anything about HTTP. That separation is what [ARCHITECTURE.md](ARCHITECTURE.md)
describes as Separation of Concerns, made concrete.
