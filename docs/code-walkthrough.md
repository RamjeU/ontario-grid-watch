# Code walkthrough

A guided tour of the codebase: where each .NET and ASP.NET Core concept is used and why. Read it alongside the code.

## Suggested reading order

1. `src/GridWatch.Core/Domain/MarketTime.cs`: how IESO hours become UTC instants.
2. `src/GridWatch.Core/Parsing/GenOutputByFuelParser.cs`: reading an IESO XML report safely.
3. `src/GridWatch.Infrastructure/Data/GridWatchDbContext.cs` and `GridRepository.cs`: the database model and upserts.
4. `src/GridWatch.Infrastructure/Ingestion/IngestionService.cs` and `IngestionWorker.cs`: how data arrives.
5. `src/GridWatch.Api/Program.cs`: how the app is wired together.
6. `src/GridWatch.Api/Controllers/GridController.cs`: the HTTP API.
7. `tests/GridWatch.Tests/Api/GridWatchApiFactory.cs`: how the whole app is tested in memory.

## Concepts and where they live

| Concept | Where | What to understand |
| --- | --- | --- |
| **Dependency injection** | `Infrastructure/DependencyInjection.cs`, `Api/Program.cs` | Services are registered once and passed in through constructors. The *lifetime* matters: `DbContext` and the services that use it are **scoped** (one per request); `TimeProvider` and the SignalR notifier are **singletons**. |
| **Primary constructors** (C# 12) | e.g. `GridQueryService(GridWatchDbContext db, TimeProvider clock)` | Constructor parameters are declared on the class line and captured as fields. |
| **Middleware pipeline** | `Program.cs` | Order matters. Forwarded headers come first so the scheme is right; then exception handling, security headers, static files, CORS and rate limiting; endpoints run last. |
| **Controllers and model binding** | `GridController.cs` | `[ApiController]` binds query strings to parameters (`DateTimeOffset? from`, `Resolution resolution`) and returns `ProblemDetails` for errors. |
| **Options pattern** | `IngestionOptions.cs` | Configuration binds to a typed class, is checked with data annotations at startup (`ValidateOnStart`), and is injected as `IOptions<T>`. |
| **EF Core** | `GridWatchDbContext.cs` | Fluent configuration for composite keys, enums stored as strings, decimal precision, and a value converter that marks every `DateTime` as UTC when it is read. |
| **Migrations** | `Infrastructure/Data/Migrations/` | Generated with `dotnet ef migrations add`. `DesignTimeDbContextFactory` tells the tools to target SQL Server. They run at startup with `MigrateAsync()`. |
| **Read vs write paths** | `GridRepository` vs `GridQueryService` | Writes track entities to detect changes. Reads use `AsNoTracking()` because nothing is modified. |
| **Background services** | `IngestionWorker.cs` | A `BackgroundService` with a `PeriodicTimer`. It creates a new DI scope on each tick, because a scoped `DbContext` must not live for the whole app. |
| **Typed HttpClient + resilience** | `IesoReportClient.cs`, `AddStandardResilienceHandler` | `IHttpClientFactory` manages connections. The resilience handler adds retries with backoff, timeouts and a circuit breaker. |
| **SignalR** | `Hubs/GridHub.cs` | The server pushes a small "data changed" signal, and clients refetch what they display. The worker depends only on the `IGridNotifier` interface, so Infrastructure does not reference SignalR. |
| **TimeProvider** | Injected everywhere that reads the clock | Tests substitute `FakeTimeProvider` to fix "now", which makes time-based logic deterministic. |
| **Async and cancellation** | Every I/O method | `CancellationToken` flows from the HTTP request or host shutdown down to EF Core and `HttpClient`. |
| **Integration testing** | `GridWatchApiFactory.cs` | `WebApplicationFactory<Program>` runs the real app in memory, swapping the database, clock and IESO HTTP handler. |

## Questions an interviewer might ask

**Why three projects instead of one?**
Dependencies point inward. `Core` has no I/O, so parsers and analytics are tested without a database or network. `Infrastructure` holds everything that talks to the outside world. `Api` is only the host. Swapping SQL Server for another database, or the web host for an Azure Function, touches one layer.

**How do you avoid duplicate rows when a report is read again?**
Natural primary keys (hour, or hour plus fuel) plus an upsert: load the existing rows in the incoming window, update the ones whose values changed and insert the rest. The IESO revises recent data, so updates matter. The behaviour is tested in `IngestionServiceTests`.

**Why convert to UTC?**
The IESO uses EST all year, while Toronto clocks switch to EDT in summer. Storing UTC gives one unambiguous timeline. Conversion happens in one place (`MarketTime`) and is tested across daylight saving dates.

**Why SQLite locally but SQL Server in production?**
SQLite needs no setup for development and tests. Production uses Azure SQL. A dedicated test applies the real migration to SQL Server in CI, so the production path is verified too. Aggregation runs in C# rather than SQL, so behaviour is identical on both.

**What happens if the IESO site is down?**
The resilience handler retries with backoff and then opens a circuit breaker. The attempt is recorded as a failed `IngestionRun` with the error, the worker logs it and tries again on the next tick, and the dashboard keeps showing the last good data with its timestamp.

**How is the admin endpoint protected?**
With an API key in a header. The key is compared as SHA-256 hashes using `CryptographicOperations.FixedTimeEquals`, so response timing does not leak partial matches. The endpoint is rate-limited to 5 requests a minute and disabled when no key is configured. A real multi-user system would use Microsoft Entra ID.

**What would you change at scale?**
Bulk `MERGE` instead of in-memory comparison, pre-aggregated daily tables, caching for the snapshot, a separate worker process so the web tier scales independently, and managed identity instead of a SQL password.

## Useful commands

```bash
dotnet test --filter "FullyQualifiedName~Parser"     # run one group of tests
dotnet ef migrations add <Name> --project src/GridWatch.Infrastructure --startup-project src/GridWatch.Api --output-dir Data/Migrations
dotnet ef migrations script --idempotent --project src/GridWatch.Infrastructure --startup-project src/GridWatch.Api
```

Before `dotnet ef` works, run `dotnet tool restore` once. The tool is pinned in `.config/dotnet-tools.json`.
