using ApiAggregator.Api.Models;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

public class SourceFetcherTests
{
    [Fact]
    public async Task Second_call_is_served_from_cache_without_calling_the_api()
    {
        var (fetcher, stats) = CreateFetcher();
        var source = FakeSource.Returning("S", Item("S", "one"));

        var first = await fetcher.GetAsync(source, "term", default);
        var second = await fetcher.GetAsync(source, "TERM ", default);   // normalised: same cache key

        Assert.Equal(SourceStatus.Live, first.Report.Status);
        Assert.Equal(SourceStatus.Cached, second.Report.Status);
        Assert.Equal(1, source.Calls);
        Assert.Equal(1, stats.GetSnapshots().Single().TotalRequests);   // cache hits are not API requests
    }

    [Fact]
    public async Task Different_search_terms_do_not_share_cache_entries()
    {
        var (fetcher, _) = CreateFetcher();
        var source = FakeSource.Returning("S", Item("S", "one"));

        await fetcher.GetAsync(source, "a", default);
        await fetcher.GetAsync(source, "b", default);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Failure_without_cached_data_is_reported_as_failed_not_thrown()
    {
        var (fetcher, stats) = CreateFetcher();

        var outcome = await fetcher.GetAsync(FakeSource.Failing("S"), "term", default);

        Assert.Equal(SourceStatus.Failed, outcome.Report.Status);
        Assert.Empty(outcome.Items);
        Assert.Contains("boom", outcome.Report.Error);
        Assert.Equal(1, stats.GetSnapshots().Single().FailedRequests);
    }

    [Fact]
    public async Task Failure_after_cache_expiry_falls_back_to_last_known_good_data()
    {
        // Fresh cache expires after 1 ms; the stale copy lives for an hour.
        var (fetcher, _) = CreateFetcher(freshDuration: TimeSpan.FromMilliseconds(1));
        var healthy = true;
        var source = new FakeSource("S", (_, _) => healthy
            ? Task.FromResult<IReadOnlyList<AggregatedItem>>([Item("S", "good")])
            : throw new HttpRequestException("down"));

        await fetcher.GetAsync(source, "term", default);
        await Task.Delay(100);
        healthy = false;

        var outcome = await fetcher.GetAsync(source, "term", default);

        Assert.Equal(SourceStatus.Stale, outcome.Report.Status);
        Assert.Equal("good", Assert.Single(outcome.Items).Title);
        Assert.Contains("down", outcome.Report.Error);
    }

    [Fact]
    public async Task Concurrent_identical_requests_result_in_a_single_api_call()
    {
        var (fetcher, _) = CreateFetcher();
        var source = new FakeSource("S", async (_, _) =>
        {
            await Task.Delay(150);
            return [Item("S", "one")];
        });

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => fetcher.GetAsync(source, "term", default))));

        Assert.Equal(1, source.Calls);
        Assert.All(outcomes, o => Assert.Single(o.Items));
    }

    [Fact]
    public async Task Cancelled_caller_gets_cancellation_without_breaking_the_shared_fetch()
    {
        var (fetcher, _) = CreateFetcher();
        var gate = new TaskCompletionSource();
        var source = new FakeSource("S", async (_, _) =>
        {
            await gate.Task;
            return [Item("S", "one")];
        });

        using var cts = new CancellationTokenSource();
        var cancelled = fetcher.GetAsync(source, "term", cts.Token);
        var patient = fetcher.GetAsync(source, "term", default);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        gate.SetResult();
        Assert.Equal(SourceStatus.Live, (await patient).Report.Status);
        Assert.Equal(1, source.Calls);
    }
}
