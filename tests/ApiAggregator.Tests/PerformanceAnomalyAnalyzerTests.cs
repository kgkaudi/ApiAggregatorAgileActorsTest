using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Statistics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ApiAggregator.Tests;

public class PerformanceAnomalyAnalyzerTests
{
    private static (InMemoryRequestStatsStore Store, PerformanceAnomalyAnalyzer Analyzer, FakeTimeProvider Clock) Create()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemoryRequestStatsStore(Options.Create(new StatsOptions()), clock);
        var analyzer = new PerformanceAnomalyAnalyzer(store, Options.Create(new AnomalyDetectionOptions()));
        return (store, analyzer, clock);
    }

    private static void Record(InMemoryRequestStatsStore store, string api, double ms, int times)
    {
        for (var i = 0; i < times; i++) store.Record(api, TimeSpan.FromMilliseconds(ms), true);
    }

    [Fact]
    public void Detects_api_whose_recent_average_is_over_50_percent_above_overall()
    {
        var (store, analyzer, clock) = Create();

        Record(store, "Slowing", 50, 20);          // historical baseline
        clock.Advance(TimeSpan.FromMinutes(6));    // baseline leaves the 5 minute window
        Record(store, "Slowing", 500, 5);          // recent: 500ms vs overall 140ms

        var anomaly = Assert.Single(analyzer.Analyze());
        Assert.Equal("Slowing", anomaly.Api);
        Assert.Equal(500, anomaly.RecentAverageMs);
        Assert.Equal(140, anomaly.OverallAverageMs);
    }

    [Fact]
    public void Does_not_flag_stable_apis()
    {
        var (store, analyzer, clock) = Create();
        Record(store, "Stable", 100, 20);
        clock.Advance(TimeSpan.FromMinutes(6));
        Record(store, "Stable", 110, 5);

        Assert.Empty(analyzer.Analyze());
    }

    [Fact]
    public void Ignores_windows_with_too_few_samples()
    {
        var (store, analyzer, clock) = Create();
        Record(store, "Sparse", 50, 20);
        clock.Advance(TimeSpan.FromMinutes(6));
        Record(store, "Sparse", 5000, 2);   // below MinimumSamples (3)

        Assert.Empty(analyzer.Analyze());
    }
}
