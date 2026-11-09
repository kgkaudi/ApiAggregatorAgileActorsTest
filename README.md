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

## Testing with Postman

A ready-made collection is included: [`ApiAggregator.postman_collection.json`](ApiAggregator.postman_collection.json).

1. Start the API: `dotnet run --project src/ApiAggregator.Api`
2. In Postman choose **Import** and select the collection file.
3. Check the collection variables (collection → **Variables** tab):

   | Variable | Default | Purpose |
   |---|---|---|
   | `baseUrl` | `http://localhost:5000` | Where the API is running |
   | `username` / `password` | `demo` / `demo-password` | Demo credentials from `appsettings.json` |
   | `token`, `tokenExpiresAt` | *(filled automatically)* | JWT used by every request |

4. Send any request. A collection-level pre-request script logs in automatically and refreshes the JWT
   shortly before it expires, so no manual login step is needed. You can also run the whole collection with
   the **Collection Runner**; every request has test assertions.

| Folder | Requests |
|---|---|
| *(root)* | `Health` |
| **Auth** | Get token; wrong password (401); aggregation without token (401) |
| **Aggregation** | Basic search; category filter; News newest-first; sort by title / relevance; subset of sources; date range; paging |
| **Validation errors (expect 400)** | missing query; query too short; unknown source; `pageSize` too large; `from` after `to`; invalid `sortBy` |
| **Statistics** | Request statistics (buckets add up to totals); stats without token (401) |

Handy demos:

- **Caching:** send *Basic search* twice. The first response shows `"status": "Live"` per source, the second
  `"status": "Cached"` with `durationMs: 0`.
- **Statistics:** run a few searches, then *Request statistics* to see totals and fast / average / slow buckets.
- **Fallback:** query once while online, lower `Aggregation:CacheDuration` (e.g. `00:00:05`), go offline and query
  again: sources come back as `Stale` with data still returned.

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
| **Cache stampede** | Single-flight: concurrent identical requests share one in-flight API call (`ConcurrentDictionary<string, Lazy<Task>>`). The shared call is bounded by `SourceTimeout` and is not cancelled when one caller disconnects. |
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

External APIs are never called from tests.

The Postman collection complements these with end-to-end checks against a running instance (real external APIs, auth, validation, statistics).

## Known limitations / possible extensions

- Statistics and caches are per-process and lost on restart (by design for the exercise); use Redis / a metrics backend for multi-instance deployments.
- The stats endpoint measures live API calls including retries, not per-attempt latency.
- Anomaly baseline is the lifetime average (as in the brief); an exponentially weighted baseline would adapt better to long-term drift.
- Credentials are config-based for the demo; swap `AuthController` for a real identity provider.
# ApiAggregatorAgileActorsTest
