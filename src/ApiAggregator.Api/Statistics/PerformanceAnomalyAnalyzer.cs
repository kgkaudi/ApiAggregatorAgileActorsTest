using ApiAggregator.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Statistics;

public sealed record PerformanceAnomaly(string Api, double RecentAverageMs, double OverallAverageMs, int RecentSampleCount);

/// <summary>
/// Pure detection logic (no timers, no logging) so it can be unit tested easily.
/// An API is anomalous when its average response time within the recent window exceeds
/// its overall average by more than the configured percentage.
/// </summary>
public sealed class PerformanceAnomalyAnalyzer(IRequestStatsStore store, IOptions<AnomalyDetectionOptions> options)
{
    public IReadOnlyList<PerformanceAnomaly> Analyze()
    {
        var o = options.Value;
        var factor = 1 + o.DegradationThresholdPercent / 100.0;
        var anomalies = new List<PerformanceAnomaly>();

        foreach (var snapshot in store.GetSnapshots())
        {
            var recent = store.GetRecent(snapshot.Api, o.Window);
            if (recent.Count < o.MinimumSamples || snapshot.AverageResponseTimeMs <= 0) continue;

            if (recent.AverageResponseTimeMs > snapshot.AverageResponseTimeMs * factor)
                anomalies.Add(new PerformanceAnomaly(snapshot.Api, recent.AverageResponseTimeMs, snapshot.AverageResponseTimeMs, recent.Count));
        }

        return anomalies;
    }
}
