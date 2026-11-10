# API Aggregator

An ASP.NET Core (.NET 10) service that queries several external APIs in parallel, merges the results into one
unified model, and exposes them through a single endpoint with filtering, sorting and paging. It also exposes
per-API request statistics and logs performance anomalies in the background.

External APIs used (all free, **no API key required**):

| Source       | API                                   | Category    |
|--------------|---------------------------------------|-------------|
| `GitHub`     | GitHub REST search (repositories)     | `Code`      |
| `HackerNews` | Algolia HN search API (stories)       | `News`      |
| `Wikipedia`  | MediaWiki search API                  | `Reference` |

## Requirements

This project implements the "API Aggregation" assignment for a .NET developer. The brief asks for an ASP.NET Core
service that consolidates data from several external APIs behind one endpoint. The table maps every requirement
to its implementation.

### Functional and technical requirements

| # | Requirement | How it is met | Where |
|---|---|---|---|
| 1 | .NET API aggregation service using ASP.NET Core | ASP.NET Core Web API on .NET 10 with controllers | `src/ApiAggregator.Api` |
| 2 | Architecture that allows easy integration of new APIs | Every API implements one small interface, `IDataSource`. Caching, retries, fallback and statistics wrap it generically, so a new API is one class plus one registration line | `Sources/IDataSource.cs`, `Extensions/ServiceCollectionExtensions.cs` |
| 3 | Fetch from at least three external APIs simultaneously | GitHub, Hacker News (Algolia) and Wikipedia, all queried at the same time with `Task.WhenAll` | `Sources/*DataSource.cs`, `Services/AggregationService.cs` |
| 4 | Endpoint to retrieve the aggregated data | `GET /api/aggregation` | `Controllers/AggregationController.cs` |
| 5 | Filter and sort by parameters (date, relevance, category) | `category`, `sources`, `from`/`to` filters; `sortBy` (Date, Relevance, Title, Source) and `order`; plus paging | `Services/ItemFilterSorter.cs`, `Controllers/AggregationRequest.cs` |
| 6 | Error handling, transient-failure handling, fallback when an API is unavailable | Retry with backoff, timeouts and a circuit breaker per API. If a call still fails, the last good response is served and marked `Stale`. If there is none, only that source is `Failed` and the others still answer | `Extensions/ServiceCollectionExtensions.cs`, `Services/SourceFetcher.cs` |
| 7 | Unit tests | 100+ xUnit tests (see [Tests](#tests)) | `tests/ApiAggregator.Tests` |
| 8 | Documentation: endpoints, input/output formats, setup and configuration | This README, Swagger UI (`/swagger`) and the Postman collection | `README.md`, `ApiAggregator.postman_collection.json` |
| 9 | Caching to optimise performance and minimise redundant API calls | In-memory cache per source and search term (2 min), plus single-flight so concurrent identical requests share one upstream call | `Services/SourceFetcher.cs` |
| 10 | Parallelism to decrease response times | Sources are called concurrently, so latency is about the slowest source rather than the sum | `Services/AggregationService.cs` |
| 11 | Endpoint with request statistics: per API total requests and average response time, grouped in performance buckets | `GET /api/stats` returns per API total and failed requests, average time, and fast (< 100 ms) / average (100-200 ms) / slow (> 200 ms) buckets. Thresholds are configurable. Data is stored in memory and is thread-safe | `Controllers/StatsController.cs`, `Statistics/InMemoryRequestStatsStore.cs` |

### Optional requirements (both implemented)

| Requirement | How it is met | Where |
|---|---|---|
| Secure the API with JWT bearer authentication | `POST /api/auth/token` issues a signed JWT. The aggregation and stats endpoints require it | `Authentication/JwtTokenService.cs`, `Controllers/AuthController.cs`, `Program.cs` |
| Background service that logs performance anomalies (5-minute average more than 50 % above the API's average) | A `BackgroundService` runs every minute and logs a warning for each anomalous API. The detection logic is separate and unit tested | `Statistics/PerformanceAnomalyWorker.cs`, `Statistics/PerformanceAnomalyAnalyzer.cs` |

### How the evaluation criteria are addressed

| Criterion | Approach |
|---|---|
| Multiple APIs integrated, aggregated data returned | Three live APIs mapped into one `AggregatedItem` model |
| Code structure, readability, maintainability | Small single-purpose classes, interfaces at the seams, options classes for configuration, no logic in controllers |
| Async programming and scalability | Async end to end (no blocking calls), parallel fan-out, `HttpClientFactory` typed clients, bounded memory in the stats store |
| Error handling and fallback | Resilience pipeline plus stale-data fallback; one failing API never fails the whole request |
| Unit test completeness | Stats, anomaly detection, filtering/sorting, caching and fallback, parallelism, JSON mapping, JWT and HTTP pipeline |
| Documentation | README, Swagger and Postman collection |
| In-memory stats with concurrency safety | Per-API accumulators guarded by a short lock, verified by a 40 000-write concurrent test |
| Performance optimisations | Caching, single-flight de-duplication, parallel calls |
| JWT security (optional) | Signature, issuer, audience and lifetime validated; signing key checked at startup |
| Anomaly logging (optional) | Periodic background analysis with a minimum-sample guard against noise |

## Quick start

Requirements: .NET 10 SDK.

```bash
dotnet restore
dotnet test
dotnet run --project src/ApiAggregator.Api
```

The API listens on `http://localhost:5000` (printed on startup). Swagger UI is at `http://localhost:5000/swagger`.
To try it with curl:

```bash
# 1. get a token (demo credentials from appsettings.json -> "Auth")
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"demo","password":"demo-password"}' | jq -r .accessToken)

# 2. query
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/aggregation?query=dotnet&sortBy=date&order=desc&pageSize=10"

# 3. statistics
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/stats
```

## Working in VS Code

The repository ships with VS Code settings (`.vscode/`), so you only need VS Code, the .NET 10 SDK and Postman.

1. Open the repository folder in VS Code. Accept the prompt to install the recommended extensions:
   **C# Dev Kit**, **C#** and the **Postman** extension (`.vscode/extensions.json`).
2. **Run and debug:** press **F5** (configuration *API: run and debug*). It builds the solution, starts the API on
   `http://localhost:5000` in the Development environment and opens Swagger. Breakpoints work in controllers, services and sources.
3. **Run the tests:** open the **Testing** panel (beaker icon). Every test is listed with run and debug buttons.
   From the keyboard, **Ctrl+Shift+B** builds and *Terminal > Run Task > test* runs all tests.
4. **Fast edit loop:** *Run Task > watch (run with hot reload)* restarts the API automatically when you save a file.
5. **Call the API from Postman:**
   1. Import `ApiAggregator.postman_collection.json` and `ApiAggregator.local.postman_environment.json`.
   2. Select the **API Aggregator - Local** environment (top-right dropdown).
   3. With the API running (step 2), send any request. Login is automatic.
6. **Run the Postman tests without the app:** `npx newman run ApiAggregator.postman_collection.json -e ApiAggregator.local.postman_environment.json`

| Task | Shortcut / command |
|---|---|
| Build | `Ctrl+Shift+B` or `dotnet build` |
| Debug the API | `F5` |
| Run all tests | Testing panel, or `dotnet test` |
| Run without the debugger | `dotnet run --project src/ApiAggregator.Api` (uses `Properties/launchSettings.json`: Development, port 5000) |

## Testing with Postman

A ready-made collection is included: [`ApiAggregator.postman_collection.json`](ApiAggregator.postman_collection.json).

1. Start the API: `dotnet run --project src/ApiAggregator.Api`
2. In Postman choose **Import** and select the collection file.
3. Optional: also import `ApiAggregator.local.postman_environment.json` and select the **API Aggregator - Local**
   environment. It only defines `baseUrl`, `username` and `password`. Do **not** add `token` to an environment:
   environment values override collection variables, so an empty environment `token` would hide the one the
   collection fetches and every request would get `401`.
4. Check the variables (collection → **Variables** tab, or the environment):

   | Variable | Default | Purpose |
   |---|---|---|
   | `baseUrl` | `http://localhost:5000` | Where the API is running |
   | `username` / `password` | `demo` / `demo-password` | Demo credentials from `appsettings.json` |
   | `token`, `tokenExpiresAt` | *(filled automatically)* | JWT used by every request |

5. Send any request. A collection-level pre-request script logs in automatically and refreshes the JWT
   shortly before it expires, so no manual login step is needed. You can also run the whole collection with
   the **Collection Runner**; every request has test assertions.

| Folder | Requests |
|---|---|
| *(root)* | `Health` |
| **Auth** | Get token; wrong password (401); aggregation without token (401) |
| **Aggregation** | Basic search; category filter; News newest-first; sort by title / relevance (order is verified); subset of sources (every item must come from a requested source); date range; paging; page past the last page; category with no matches |
| **Caching** | Two dependent requests: a brand-new unique term must never be `Cached`, then repeating it must return `Cached` with `durationMs: 0` for every source that answered live |
| **Validation errors (expect 400)** | missing query; query too short; unknown source; `pageSize` too large; `from` after `to`; invalid `sortBy` |
| **Statistics** | Request statistics (buckets add up to totals); stats without token (401) |

Handy demos:

- **Caching:** run the **Caching** folder (or the whole collection with the Runner). Request 1 uses a unique
  term, request 2 repeats it and asserts that each source that was `Live` is now `Cached` with `durationMs: 0`.
  The two requests depend on each other, so keep them in order.
- **Statistics:** run a few searches, then *Request statistics* to see totals and fast / average / slow buckets.
- **Fallback:** query once while online, lower `Aggregation:CacheDuration` (e.g. `00:00:05`), go offline and query
  again: sources come back as `Stale` with data still returned.

Every request also checks that the response arrives within 20 seconds (a collection-level test). The statistics
request verifies that bucket counts add up to the totals, that failures never exceed totals and that averages are not negative.

Troubleshooting:

- **`401` on every request:** the selected environment defines `token`. Remove that variable (or re-import the
  environment from this repository).
- **Connection refused / no response:** the API is not running, or `baseUrl` does not match its port.
- **A source shows `Failed` with 403 or 429:** GitHub's anonymous rate limit. Wait a minute or set
  `ExternalApis__GitHub__Token`. The caching test only asserts on sources that answered `Live`, so it still passes.
- **Caching request 2 fails:** run request 1 first and send request 2 within the cache lifetime (2 minutes by default).

## Endpoints

### `POST /api/auth/token` (anonymous)
Body `{ "username": "...", "password": "..." }` → `{ "accessToken", "tokenType": "Bearer", "expiresAt" }`.
`401` on bad credentials. Demo credentials live in configuration only to keep the exercise self-contained.

### `GET /api/aggregation` (JWT required)

| Parameter  | Description                                                         | Default     |
|------------|---------------------------------------------------------------------|-------------|
| `query`    | **Required.** Search term (2–100 chars) sent to every source        |             |
| `category` | Filter: `Code`, `News`, `Reference`                                 | all         |
| `sources`  | Comma-separated subset, e.g. `GitHub,Wikipedia`                     | all         |
| `from`/`to`| Inclusive ISO-8601 date range on `publishedAt` (use `Z`, not `+`)   |             |
| `sortBy`   | `Date`, `Relevance`, `Title`, `Source`                              | `Relevance` |
| `order`    | `Asc`, `Desc`                                                       | `Desc`      |
| `page`, `pageSize` | Paging (`pageSize` ≤ 100)                                   | 1, 20       |

Response:

```json
{
  "query": "dotnet",
  "totalItems": 57,
  "page": 1,
  "pageSize": 10,
  "items": [
    {
      "source": "GitHub",
      "title": "dotnet/runtime",
      "description": ".NET is a cross-platform runtime · stars: 15000 · C#",
      "url": "https://github.com/dotnet/runtime",
      "category": "Code",
      "publishedAt": "2024-05-01T10:00:00+00:00",
      "relevance": 1.0
    }
  ],
  "sources": [
    { "source": "GitHub",     "status": "Live",   "itemCount": 20, "durationMs": 312, "dataRetrievedAt": "...", "error": null },
    { "source": "Wikipedia",  "status": "Cached", "itemCount": 20, "durationMs": 0,   "dataRetrievedAt": "...", "error": null },
    { "source": "HackerNews", "status": "Stale",  "itemCount": 17, "durationMs": 5400,"dataRetrievedAt": "...", "error": "HttpRequestException: ..." }
  ]
}
```

`status` per source: `Live` (called the API), `Cached`, `Stale` (API failed → last known good data served),
`Failed` (API failed and nothing to fall back on).
Status codes: `200`; `400` invalid parameters; `401` missing/invalid token;
`503` only when **every** source failed with no fallback (body still contains the per-source errors).

`relevance` is a 0–1 score derived from the item's position in its own source's relevance ranking, so
items are comparable across sources. Items without a date sort last on `sortBy=date&order=desc` and are
excluded when `from`/`to` is used.

### `GET /api/stats` (JWT required)

```json
{
  "generatedAt": "...",
  "thresholds": { "fastBelowMs": 100, "averageUpToMs": 200 },
  "apis": [
    {
      "api": "GitHub",
      "totalRequests": 12,
      "failedRequests": 1,
      "averageResponseTimeMs": 183.4,
      "buckets": {
        "fast":    { "count": 3, "averageResponseTimeMs": 72.1 },
        "average": { "count": 6, "averageResponseTimeMs": 151.8 },
        "slow":    { "count": 3, "averageResponseTimeMs": 330.0 }
      }
    }
  ]
}
```
Only *live* calls to the external API are counted (cache hits never reach the API). Failed calls are counted
and included in the averages, because slow failures (timeouts) are exactly what you want to see.

### `GET /health` (anonymous)

## Project structure

```
ApiAggregator/
├── ApiAggregator.sln
├── README.md
├── ApiAggregator.postman_collection.json
├── ApiAggregator.local.postman_environment.json
├── .vscode/
├── .gitignore
├── src/
│   └── ApiAggregator.Api/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── ApiAggregator.Api.csproj
│       ├── Controllers/
│       ├── Services/
│       ├── Sources/
│       ├── Statistics/
│       ├── Authentication/
│       ├── Configuration/
│       ├── Models/
│       └── Extensions/
└── tests/
    └── ApiAggregator.Tests/
```

### Root

| File | Purpose |
|---|---|
| `ApiAggregator.sln` | Visual Studio / `dotnet` solution that ties the API and test projects together |
| `README.md` | This documentation |
| `ApiAggregator.postman_collection.json` | Postman collection with requests and test assertions for every endpoint |
| `ApiAggregator.local.postman_environment.json` | Postman environment for a locally running API (base URL and demo login) |
| `.vscode/launch.json` | F5 debug configuration: builds, runs the API in Development on port 5000 and opens Swagger |
| `.vscode/tasks.json` | Build, test and hot-reload (`dotnet watch`) tasks |
| `.vscode/extensions.json` | Recommended VS Code extensions (C# Dev Kit, C#, Postman) |
| `.gitignore` | Keeps `bin/`, `obj/` and IDE files out of the repository |

### `src/ApiAggregator.Api` (the service)

| File | What it does |
|---|---|
| `Program.cs` | Application entry point and composition root. Registers configuration, caching, statistics, data sources, JWT authentication, Swagger and the background worker, then builds the HTTP pipeline. Exposes `public partial class Program` so integration tests can host the app |
| `Properties/launchSettings.json` | Makes `dotnet run` start in Development on `http://localhost:5000` |
| `appsettings.json` | All settings: cache durations, bucket thresholds, anomaly settings, JWT settings, demo credentials and the base URL of each external API |
| `ApiAggregator.Api.csproj` | Project file with the target framework and NuGet packages (JWT bearer, HTTP resilience, Swagger) |

**`Controllers/`** is the thin HTTP layer. It validates input and delegates, with no business logic.

| File | What it does |
|---|---|
| `AggregationController.cs` | `GET /api/aggregation`. Parses the `sources` list, rejects unknown sources and invalid date ranges with `400`, calls the aggregation service, and returns `503` only if every source failed |
| `AggregationRequest.cs` | The query-string model for the endpoint, with validation attributes (query length, page size limits) and API documentation comments |
| `StatsController.cs` | `GET /api/stats`. Returns the statistics snapshots plus the bucket thresholds in use |
| `AuthController.cs` | `POST /api/auth/token`. Checks the demo credentials in constant time and returns a JWT |

**`Services/`** holds the core aggregation logic.

| File | What it does |
|---|---|
| `IAggregationService.cs` | Contract for the aggregation use case; also exposes the registered source names for validation |
| `AggregationService.cs` | Selects the requested sources, queries them all in parallel with `Task.WhenAll`, merges the items, applies filtering/sorting, then pages the result and builds the per-source report |
| `ISourceFetcher.cs` | Contract for "get data for one source safely". It never throws for source failures |
| `SourceFetcher.cs` | The reliability layer around each source: fresh cache, single-flight de-duplication, stale-copy fallback, per-call timeout and statistics recording |
| `ItemFilterSorter.cs` | Pure functions that filter by category and date range and sort by date, relevance, title or source, with deterministic tie-breakers so paging is stable |

**`Sources/`** has one class per external API.

| File | What it does |
|---|---|
| `IDataSource.cs` | The extension point: `Name`, `Category` and `FetchAsync(searchTerm)`. Implement this to add a new API |
| `GitHubDataSource.cs` | Calls the GitHub repository search and maps results to `AggregatedItem` (category `Code`) |
| `HackerNewsDataSource.cs` | Calls the Algolia Hacker News search and maps stories (category `News`). Stories without a link point to the discussion page |
| `WikipediaDataSource.cs` | Calls the MediaWiki search and maps articles (category `Reference`), stripping HTML from snippets |
| `ExternalDtos.cs` | Internal classes that mirror each API's JSON, kept separate from the unified model |
| `RelevanceScore.cs` | Converts an item's position in its source's ranking into a 0-1 relevance score |

**`Statistics/`** covers request statistics and anomaly detection.

| File | What it does |
|---|---|
| `IRequestStatsStore.cs` | Contract plus the result types (snapshot, buckets, recent stats) |
| `InMemoryRequestStatsStore.cs` | Thread-safe in-memory store. Keeps totals, failures, bucket counts and a time-limited sample queue per API, each protected by a short lock |
| `PerformanceAnomalyAnalyzer.cs` | Pure logic that compares each API's recent average with its overall average and returns the anomalies |
| `PerformanceAnomalyWorker.cs` | `BackgroundService` that runs the analyzer on a timer and logs warnings, and never crashes on a failed run |

**`Authentication/`**

| File | What it does |
|---|---|
| `JwtTokenService.cs` | Creates signed JWTs (subject, id, issuer, audience, expiry) from the configured key |

**`Configuration/`**

| File | What it does |
|---|---|
| `AppOptions.cs` | Strongly typed options classes bound to the sections of `appsettings.json` (aggregation, statistics, anomaly detection, JWT, auth, external APIs) |

**`Models/`**

| File | What it does |
|---|---|
| `AggregatedItem.cs` | The unified item every API is mapped into (source, title, description, url, category, date, relevance) |
| `AggregationQuery.cs` | The validated domain query plus the `SortField` and `SortDirection` enums |
| `AggregationResult.cs` | The response model: items, paging info, per-source reports and the `SourceStatus` enum (Live, Cached, Stale, Failed) |

**`Extensions/`**

| File | What it does |
|---|---|
| `ServiceCollectionExtensions.cs` | `AddExternalDataSources` lists the registered APIs. `AddDataSource<T>` creates each API's `HttpClient` (base URL, headers, optional token) and attaches the resilience pipeline: retries, timeouts, circuit breaker |

### `tests/ApiAggregator.Tests`

| File | What it covers |
|---|---|
| `TestHelpers.cs` | Shared fakes and builders: a configurable fake `IDataSource`, a stub HTTP handler and a fetcher factory |
| `InMemoryRequestStatsStoreTests.cs` | Bucket boundaries, averages, failure counts, concurrent writes, recent-window queries and retention |
| `PerformanceAnomalyAnalyzerTests.cs` | Detects a slow-down, ignores stable APIs and ignores windows with too few samples |
| `ItemFilterSorterTests.cs` | Category and date filters, and every sort option and direction |
| `SourceFetcherTests.cs` | Cache hits, key normalisation, failure reporting, stale fallback, single-flight and cancellation |
| `AggregationServiceTests.cs` | Merging, parallel execution, partial and total failure, source selection and paging |
| `DataSourceTests.cs` | JSON-to-model mapping for each API using stubbed HTTP responses, and error propagation |
| `JwtTokenServiceTests.cs` | Tokens validate with the right key, carry the subject and expiry, and are rejected with a wrong key |
| `ApiIntegrationTests.cs` | Runs the real HTTP pipeline in memory: 401 without a token, token endpoint, stats endpoint and 400 validation |
| `ApiEndToEndTests.cs` | Full pipeline with fake sources: merged 200 responses, filtering, sorting, date ranges, paging (including past the last page), partial failure (200), total failure (503), every invalid-parameter case (400), stats accuracy and one upstream call for 10 simultaneous requests |
| `SourceFetcherResilienceTests.cs` | Source timeouts (including sources that ignore cancellation; failed, or stale when a copy exists), failed calls are never cached, recovery from `Failed` and `Stale` back to `Live`, empty results, and every kind of exception becoming `Failed` |
| `DataSourceEdgeCaseTests.cs` | Empty, `null` and malformed upstream JSON, entries with missing fields, non-ASCII titles, and URL-encoding of special characters in search terms |
| `JwtTokenEdgeCaseTests.cs` | Expired, not-yet-valid, wrong-issuer, wrong-audience, forged and `alg: none` tokens, plus the same rules through the real pipeline (401) and incomplete credentials on the token endpoint (400) |

## Request flow

What happens on `GET /api/aggregation?query=dotnet`:

1. **Authentication.** The JWT bearer middleware validates the token; otherwise the response is `401`.
2. **Controller.** `AggregationController` validates the parameters and builds an `AggregationQuery`.
3. **Fan-out.** `AggregationService` starts `SourceFetcher.GetAsync` for every selected source at the same time.
4. **Per source, `SourceFetcher`:**
   1. Returns the fresh cached copy if there is one (`Cached`).
   2. Otherwise joins an in-flight call for the same source and term, or starts one.
   3. The `IDataSource` calls its external API through an `HttpClient` with the resilience pipeline (retries, timeouts, circuit breaker).
   4. On success it records the duration in the stats store, caches the result and returns `Live`.
   5. On failure it records the failure, then returns the last good copy as `Stale`, or `Failed` if there is none.
5. **Merge.** `AggregationService` combines all items, then `ItemFilterSorter` filters and sorts them, and the page is cut out.
6. **Response.** The items and the per-source report are returned. Meanwhile `PerformanceAnomalyWorker` reads the same stats store in the background.

## Architecture

```
Controller ──► AggregationService ──(Task.WhenAll)──► SourceFetcher ──► IDataSource (GitHub | HackerNews | Wikipedia)
                    │                                    │  │  │                │
              ItemFilterSorter                  fresh cache │  └ stats store    └ HttpClient + resilience pipeline
                (filter → sort → page)          stale cache └ single-flight       (retry, timeouts, circuit breaker)
```

| Concern | Where / how |
|---|---|
| **Adding a new API** | Implement `IDataSource` (call API + map to `AggregatedItem`), add DTOs, add `ExternalApis:<Name>` to config and one line in `AddExternalDataSources`. No other code changes. |
| **Parallelism** | `AggregationService` starts all source fetches at once with `Task.WhenAll`; latency ≈ slowest source. Fully async/await, no blocking calls. |
| **Transient failures** | Per-source `HttpClient` with `AddStandardResilienceHandler`: 2 retries (exponential backoff + jitter) on 5xx/408/429/network errors, 5 s per-attempt timeout, 14 s total timeout, circuit breaker. |
| **Fallback** | `SourceFetcher` keeps the last good response per source+term for 1 h. If a live call fails, that copy is served and flagged `Stale`. With no copy the source is reported `Failed` and the other sources still answer. |
| **Caching** | `IMemoryCache`, key = source + normalised term. Fresh TTL 2 min. Filters/sorting/paging are applied *after* the cache, so different filters on the same term reuse the same cached upstream data. |
| **Cache stampede** | Single-flight: concurrent identical requests share one in-flight API call (`ConcurrentDictionary<string, Lazy<Task>>`). The shared call is bounded by `SourceTimeout` (enforced even if a source ignores its cancellation token) and is not cancelled when one caller disconnects. |
| **Statistics & thread-safety** | `InMemoryRequestStatsStore`: `ConcurrentDictionary` of per-API accumulators; each accumulator updates its counters and recent-sample queue under a short private lock, so snapshots are always internally consistent and APIs never contend with each other. Sample retention and count are capped (bounded memory). Time is injected via `TimeProvider`. |
| **Anomaly detection** | `PerformanceAnomalyAnalyzer` (pure, unit-tested) compares the last-5-minute average with the API's overall average; > +50 % ⇒ anomaly. `PerformanceAnomalyWorker` (`BackgroundService` + `PeriodicTimer`) runs it every minute and logs a warning. Needs ≥ 3 samples in the window to avoid noise. |
| **Security** | JWT bearer (HS256) with issuer/audience/lifetime/signature validation. Signing key validated on startup (≥ 32 chars). Constant-time credential comparison. |
| **Errors** | `[ApiController]` validation → RFC 7807 problem details; unhandled exceptions → problem details via `UseExceptionHandler`. |

## Configuration (`appsettings.json`)

| Section | Key settings |
|---|---|
| `Aggregation` | `CacheDuration`, `FallbackCacheDuration`, `SourceTimeout`, `MaxItemsPerSource` |
| `Statistics` | `FastThresholdMs` (100), `AverageThresholdMs` (200), `SampleRetention`, `MaxSamplesPerApi` |
| `AnomalyDetection` | `Enabled`, `Interval`, `Window`, `DegradationThresholdPercent`, `MinimumSamples` |
| `Jwt` | `Issuer`, `Audience`, `SigningKey`, `ExpiryMinutes` |
| `Auth` | demo `Username` / `Password` |
| `ExternalApis:<Name>` | `BaseUrl`, optional `Token` (sent as bearer) |

Override with environment variables (`Jwt__SigningKey=...`) or user secrets. **Change `Jwt:SigningKey` and the
demo credentials for anything beyond local use.**

GitHub's unauthenticated search is limited to ~10 requests/min; set `ExternalApis__GitHub__Token` to a
personal access token for higher limits. Caching and the stale fallback soften this, and a rate-limited call
shows up as a `Stale`/`Failed` source rather than an error.

## Tests

`dotnet test` runs xUnit tests covering:

- stats store: bucket boundaries, averages, failures, 40 000 concurrent writes, time-window queries, retention
- anomaly analyzer: detects slow-down, ignores stable APIs and sparse windows
- filtering / sorting / paging
- `SourceFetcher`: caching, key normalisation, error → `Failed`, fallback → `Stale`, single-flight, cancellation
- `AggregationService`: merging, **parallel execution** (barrier-based, deterministic), partial and total failure, source selection
- each data source's JSON mapping (stubbed `HttpMessageHandler`), including HTTP error → exception
- JWT creation/validation and the auth/validation behaviour of the HTTP pipeline (`WebApplicationFactory`)
- **edge cases:** upstream timeouts, empty / `null` / malformed upstream data, failed calls not being cached, recovery after an outage, expired / forged / wrong-key / `alg: none` JWTs, and every invalid-parameter combination returning `400` without touching an external API
- **end-to-end:** the real HTTP pipeline with fake sources, covering `200`, `503`, paging past the end, statistics accuracy and request de-duplication

External APIs are never called from tests.

The Postman collection complements these with end-to-end checks against a running instance (real external APIs, auth, validation, statistics).

## Known limitations / possible extensions

- Statistics and caches are per-process and lost on restart (by design for the exercise); use Redis / a metrics backend for multi-instance deployments.
- The stats endpoint measures live API calls including retries, not per-attempt latency.
- Anomaly baseline is the lifetime average (as in the brief); an exponentially weighted baseline would adapt better to long-term drift.
- Credentials are config-based for the demo; swap `AuthController` for a real identity provider.
