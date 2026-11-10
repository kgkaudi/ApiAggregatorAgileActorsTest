using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Statistics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ApiAggregator.Tests;

public class InMemoryRequestStatsStoreTests
{
    private static InMemoryRequestStatsStore Create(FakeTimeProvider? clock = null, StatsOptions? options = null) =>
        new(Options.Create(options ?? new StatsOptions()), clock ?? new FakeTimeProvider());

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Classifies_requests_into_fast_average_and_slow_buckets()
    {
        var store = Create();
        store.Record("A", Ms(50), true);    // fast   (< 100)
        store.Record("A", Ms(99), true);    // fast
        store.Record("A", Ms(100), true);   // average (100-200)
        store.Record("A", Ms(200), true);   // average
        store.Record("A", Ms(201), true);   // slow   (> 200)

        var s = Assert.Single(store.GetSnapshots());
        Assert.Equal(5, s.TotalRequests);
        Assert.Equal(2, s.Buckets.Fast.Count);
        Assert.Equal(2, s.Buckets.Average.Count);
        Assert.Equal(1, s.Buckets.Slow.Count);
        Assert.Equal(74.5, s.Buckets.Fast.AverageResponseTimeMs);
        Assert.Equal(150, s.Buckets.Average.AverageResponseTimeMs);
        Assert.Equal(201, s.Buckets.Slow.AverageResponseTimeMs);
    }

    [Fact]
    public void Calculates_overall_average_and_failures_per_api()
    {
        var store = Create();
        store.Record("A", Ms(100), true);
        store.Record("A", Ms(300), false);
        store.Record("B", Ms(10), true);

        var snapshots = store.GetSnapshots();
        Assert.Equal(2, snapshots.Count);

        var a = snapshots.Single(s => s.Api == "A");
        Assert.Equal(2, a.TotalRequests);
        Assert.Equal(1, a.FailedRequests);
        Assert.Equal(200, a.AverageResponseTimeMs);

        var b = snapshots.Single(s => s.Api == "B");
        Assert.Equal(1, b.TotalRequests);
        Assert.Equal(0, b.FailedRequests);
    }

    [Fact]
    public async Task Is_thread_safe_under_concurrent_writes()
    {
        var store = Create();
        const int perThread = 5_000;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
                store.Record("A", Ms(i % 3 == 0 ? 50 : i % 3 == 1 ? 150 : 250), true);
        })));

        var s = Assert.Single(store.GetSnapshots());
        Assert.Equal(8 * perThread, s.TotalRequests);
        Assert.Equal(s.TotalRequests, s.Buckets.Fast.Count + s.Buckets.Average.Count + s.Buckets.Slow.Count);
    }

    [Fact]
    public void Recent_stats_only_include_samples_inside_the_window()
    {
        var clock = new FakeTimeProvider();
        var store = Create(clock);

        store.Record("A", Ms(100), true);
        clock.Advance(TimeSpan.FromMinutes(6));
        store.Record("A", Ms(300), true);
        store.Record("A", Ms(500), true);

        var recent = store.GetRecent("A", TimeSpan.FromMinutes(5));
        Assert.Equal(2, recent.Count);
        Assert.Equal(400, recent.AverageResponseTimeMs);
    }

    [Fact]
    public void Samples_older_than_retention_are_discarded_but_totals_are_kept()
    {
        var clock = new FakeTimeProvider();
        var store = Create(clock, new StatsOptions { SampleRetention = TimeSpan.FromMinutes(10) });

        store.Record("A", Ms(100), true);
        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(0, store.GetRecent("A", TimeSpan.FromMinutes(60)).Count);
        Assert.Equal(1, store.GetSnapshots().Single().TotalRequests);
    }

    [Fact]
    public void Unknown_api_has_no_recent_stats()
    {
        Assert.Equal(0, Create().GetRecent("nope", TimeSpan.FromMinutes(5)).Count);
    }
}
