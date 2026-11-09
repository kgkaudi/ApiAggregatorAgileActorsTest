using System.Collections.Concurrent;
using System.Diagnostics;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Models;
using ApiAggregator.Api.Sources;
using ApiAggregator.Api.Statistics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Services;

internal sealed record CachedPayload(IReadOnlyList<AggregatedItem> Items, DateTimeOffset RetrievedAt);

/// <summary>
/// Wraps one source call with:
///  1. a short-lived "fresh" cache (avoids redundant API calls),
///  2. single-flight de-duplication (N concurrent identical requests => 1 API call),
///  3. a long-lived "stale" copy of the last good response used as fallback when the API fails,
///  4. statistics recording for every live API call.
/// Transient failures (retries, timeouts, circuit breaker) are handled below this layer by the
/// HttpClient resilience pipeline configured in <c>AddDataSource</c>.
/// </summary>
public sealed class SourceFetcher(
    IMemoryCache cache,
    IRequestStatsStore stats,
    IOptions<AggregationOptions> options,
    TimeProvider clock,
    ILogger<SourceFetcher> logger) : ISourceFetcher
{
    private readonly ConcurrentDictionary<string, Lazy<Task<SourceOutcome>>> _inFlight = new();

    public async Task<SourceOutcome> GetAsync(IDataSource source, string searchTerm, CancellationToken cancellationToken)
    {
        var term = searchTerm.Trim();
        var key = $"{source.Name}|{term.ToLowerInvariant()}";

        if (cache.TryGetValue(FreshKey(key), out CachedPayload? hit) && hit is not null)
            return ToOutcome(source, SourceStatus.Cached, hit, 0, null);

        // The shared fetch is not tied to any single caller token: if the first caller disconnects,
        // the others waiting on the same fetch must still get their result. It is bounded by SourceTimeout.
        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<SourceOutcome>>(() => FetchLiveAsync(source, term, key)));
        return await lazy.Value.WaitAsync(cancellationToken);
    }

    private async Task<SourceOutcome> FetchLiveAsync(IDataSource source, string term, string key)
    {
        var opts = options.Value;
        try
        {
            // Re-check: another fetch may have completed between the caller cache check and now.
            if (cache.TryGetValue(FreshKey(key), out CachedPayload? hit) && hit is not null)
                return ToOutcome(source, SourceStatus.Cached, hit, 0, null);

            var started = Stopwatch.GetTimestamp();
            using var cts = new CancellationTokenSource(opts.SourceTimeout, clock);

            try
            {
                var items = await source.FetchAsync(term, cts.Token);
                var elapsed = Stopwatch.GetElapsedTime(started);
                stats.Record(source.Name, elapsed, success: true);

                var payload = new CachedPayload(items, clock.GetUtcNow());
                cache.Set(FreshKey(key), payload, opts.CacheDuration);
                cache.Set(StaleKey(key), payload, opts.FallbackCacheDuration);

                return ToOutcome(source, SourceStatus.Live, payload, (long)elapsed.TotalMilliseconds, null);
            }
            catch (Exception ex)
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                stats.Record(source.Name, elapsed, success: false);
                logger.LogWarning(ex, "Source {Source} failed after {ElapsedMs:F0} ms", source.Name, elapsed.TotalMilliseconds);

                var error = $"{ex.GetType().Name}: {ex.Message}";
                if (cache.TryGetValue(StaleKey(key), out CachedPayload? stale) && stale is not null)
                    return ToOutcome(source, SourceStatus.Stale, stale, (long)elapsed.TotalMilliseconds, error);

                return new SourceOutcome(
                    new SourceReport(source.Name, SourceStatus.Failed, 0, (long)elapsed.TotalMilliseconds, null, error),
                    []);
            }
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private static SourceOutcome ToOutcome(IDataSource source, SourceStatus status, CachedPayload payload, long durationMs, string? error) =>
        new(new SourceReport(source.Name, status, payload.Items.Count, durationMs, payload.RetrievedAt, error), payload.Items);

    private static string FreshKey(string key) => "fresh:" + key;
    private static string StaleKey(string key) => "stale:" + key;
}
