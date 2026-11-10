using System.Text.Json;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Models;
using ApiAggregator.Api.Services;
using ApiAggregator.Api.Statistics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

public class SourceFetcherResilienceTests
{
    private static (SourceFetcher Fetcher, InMemoryRequestStatsStore Stats) Create(TimeSpan? timeout = null, TimeSpan? fresh = null)
    {
        var stats = new InMemoryRequestStatsStore(Options.Create(new StatsOptions()), TimeProvider.System);
        var fetcher = new SourceFetcher(
            new MemoryCache(new MemoryCacheOptions()),
            stats,
            Options.Create(new AggregationOptions
            {
                CacheDuration = fresh ?? TimeSpan.FromMinutes(5),
                FallbackCacheDuration = TimeSpan.FromHours(1),
                SourceTimeout = timeout ?? TimeSpan.FromSeconds(5)
            }),
            TimeProvider.System,
            NullLogger<SourceFetcher>.Instance);
        return (fetcher, stats);
    }

    // ---- timeouts ----

    [Fact]
    public async Task A_source_that_exceeds_the_timeout_is_reported_failed_and_does_not_hang()
    {
        var (fetcher, stats) = Create(timeout: TimeSpan.FromMilliseconds(100));
        var source = new FakeSource("Slow", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);   // only ends when the fetcher's timeout cancels it
            return [];
        });

        var outcome = await fetcher.GetAsync(source, "term", default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SourceStatus.Failed, outcome.Report.Status);
        Assert.NotNull(outcome.Report.Error);
        Assert.Equal(1, stats.GetSnapshots().Single().FailedRequests);
    }

    [Fact]
    public async Task A_source_that_ignores_cancellation_is_still_cut_off_at_the_timeout()
    {
        var (fetcher, stats) = Create(timeout: TimeSpan.FromMilliseconds(100));
        var never = new TaskCompletionSource<IReadOnlyList<AggregatedItem>>();
        var source = new FakeSource("Stubborn", (_, _) => never.Task);   // never completes and never looks at the token

        var outcome = await fetcher.GetAsync(source, "term", default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SourceStatus.Failed, outcome.Report.Status);
        Assert.Contains("Timeout", outcome.Report.Error);
        Assert.Equal(1, stats.GetSnapshots().Single().FailedRequests);
    }

    [Fact]
    public async Task A_timeout_after_a_good_response_serves_the_stale_copy()
    {
        var (fetcher, _) = Create(timeout: TimeSpan.FromMilliseconds(100), fresh: TimeSpan.FromMilliseconds(1));
        var hang = false;
        var source = new FakeSource("S", async (_, ct) =>
        {
            if (hang) await Task.Delay(Timeout.Infinite, ct);
            return [Item("S", "good")];
        });

        await fetcher.GetAsync(source, "term", default);
        await Task.Delay(100);   // let the 1 ms fresh entry expire
        hang = true;

        var outcome = await fetcher.GetAsync(source, "term", default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SourceStatus.Stale, outcome.Report.Status);
        Assert.Equal("good", Assert.Single(outcome.Items).Title);
    }

    // ---- recovery ----

    [Fact]
    public async Task A_failed_call_is_not_cached_so_the_next_call_retries_and_recovers()
    {
        var (fetcher, stats) = Create();
        var healthy = false;
        var source = new FakeSource("S", (_, _) => healthy
            ? Task.FromResult<IReadOnlyList<AggregatedItem>>([Item("S", "ok")])
            : throw new HttpRequestException("down"));

        var first = await fetcher.GetAsync(source, "term", default);
        healthy = true;
        var second = await fetcher.GetAsync(source, "term", default);
        var third = await fetcher.GetAsync(source, "term", default);

        Assert.Equal(SourceStatus.Failed, first.Report.Status);
        Assert.Equal(SourceStatus.Live, second.Report.Status);
        Assert.Equal(SourceStatus.Cached, third.Report.Status);
        Assert.Equal(2, source.Calls);

        var snapshot = stats.GetSnapshots().Single();
        Assert.Equal(2, snapshot.TotalRequests);
        Assert.Equal(1, snapshot.FailedRequests);
    }

    [Fact]
    public async Task Stale_data_is_replaced_by_live_data_once_the_api_recovers()
    {
        var (fetcher, _) = Create(fresh: TimeSpan.FromMilliseconds(1));
        var healthy = true;
        var title = "v1";
        var source = new FakeSource("S", (_, _) => healthy
            ? Task.FromResult<IReadOnlyList<AggregatedItem>>([Item("S", title)])
            : throw new HttpRequestException("down"));

        await fetcher.GetAsync(source, "term", default);
        await Task.Delay(100);

        healthy = false;
        var stale = await fetcher.GetAsync(source, "term", default);
        healthy = true;
        title = "v2";
        var live = await fetcher.GetAsync(source, "term", default);

        Assert.Equal(SourceStatus.Stale, stale.Report.Status);
        Assert.Equal("v1", Assert.Single(stale.Items).Title);
        Assert.Equal(SourceStatus.Live, live.Report.Status);
        Assert.Equal("v2", Assert.Single(live.Items).Title);
    }

    // ---- unusual but valid / invalid results ----

    [Fact]
    public async Task An_empty_result_is_a_valid_live_response_and_is_cached()
    {
        var (fetcher, _) = Create();
        var source = FakeSource.Returning("S");   // no items

        var first = await fetcher.GetAsync(source, "term", default);
        var second = await fetcher.GetAsync(source, "term", default);

        Assert.Equal(SourceStatus.Live, first.Report.Status);
        Assert.Equal(0, first.Report.ItemCount);
        Assert.Equal(SourceStatus.Cached, second.Report.Status);
        Assert.Equal(1, source.Calls);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task Any_exception_from_a_source_is_reported_as_failed_not_thrown(Type exceptionType)
    {
        var (fetcher, stats) = Create();
        var source = new FakeSource("S", (_, _) => throw (Exception)Activator.CreateInstance(exceptionType, "kaboom")!);

        var outcome = await fetcher.GetAsync(source, "term", default);

        Assert.Equal(SourceStatus.Failed, outcome.Report.Status);
        Assert.Contains("kaboom", outcome.Report.Error);
        Assert.Equal(1, stats.GetSnapshots().Single().FailedRequests);
    }
}
