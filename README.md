# Metrics API for Self-Hosted Game Server

Setup of a minimal C#/ASP.NET backend that exposes values of an existing metrics database through REST API. This builds upon an existing automated server infrastructure that is able to log these metrics - that setup can be found [here](https://github.com/andrindecsy/acore-server-infrastructure).

This is built as a personal learning project.

**Detailed documentation:**
- [Architecture](docs/ARCHITECTURE.md) - workings of a .NET WebApplication, Dependency Injection - project specific and as a concept - and data models
- [Setup Guide](docs/SETUP.md) - How to get things running
- [Data Access](docs/DATA-ACCESS.md) - MySQL to C# path, asynchronous programming and dapper
- Integration Tests - WORK IN PROGRESS - Check back later!
## Repo Structure

```
azerothcore-metrics/
├── config/
│   └── appssettings.json.example
├── docs/
│   ├── ARCHITECTURE.md
│   ├── INTEGRATION-TESTS.md
│   └── SETUP.md
├── models/
│   ├── MemoryLogEntry.cs
│   ├── ServerEvents.cs
│   └── StatusSummary.cs
├── Program.cs
├── README.md
└── ServerMetricsApi.csproj
```

## Overview

This is the continuation of my journey of setting up a persistent application server. Here we will make the valuable information our server already collects available to the outside. It is based on Microsoft's ASP.NET, MySqlConnector and Dapper for building a minimal API that will listen on a certain port and will send out the information when prompted. **This by itself is not really useful - it can be seen as an intermediate step that will allow us to make a full frontend dashboard later.** You could find any one piece of information in here useful for your own projects. It is meant more as a documentation of all the interesting facts I learned throughout and less as a comprehensive, all-encompassing guide. I treated this write-up as I would a personal blog. It includes:

- Configuration of a .NET WebApplication
- Architecture concepts: Dependency Injection/Inversion of Control, Separation of Concerns, Domain Models vs Data Transfer Objects
- Database communications and MySqlConnector explanation, including a short introduction to asynchronous programming
- Database information to C#-object mapping and Dapper explanation
- WORK IN PROGRESS: Introduction to software testing by running [integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=xunit) on the database

## API Endpoints

| Endpoint                    | Description                                            |
| --------------------------- | ------------------------------------------------------ |
| `GET /api/status`           | Current snapshot - latest measurement + online/offline |
| `GET /api/metrics?hours=24` | Time series for the last `hours` hours                 |
| `GET /api/events?days=7`    | Events from the last `days` days                       |


## Tech Stack

- **Language/Runtime:** C# 10+, .NET 10
- **Framework:** ASP.NET Core (Minimal APIs)
- **Database:** MySQL, MySqlConnector
- **ORM:** Dapper
