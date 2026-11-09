namespace ApiAggregator.Api.Statistics;

public sealed record BucketStats(long Count, double AverageResponseTimeMs);

public sealed record PerformanceBuckets(BucketStats Fast, BucketStats Average, BucketStats Slow);

public sealed record ApiStatsSnapshot(
    string Api,
    long TotalRequests,
    long FailedRequests,
    double AverageResponseTimeMs,
    PerformanceBuckets Buckets);

public sealed record RecentStats(int Count, double AverageResponseTimeMs);

/// <summary>Thread-safe store of per-API request statistics.</summary>
public interface IRequestStatsStore
{
    void Record(string api, TimeSpan duration, bool success);
    IReadOnlyList<ApiStatsSnapshot> GetSnapshots();

    /// <summary>Count and average duration of requests recorded within the last <paramref name="window"/>.</summary>
    RecentStats GetRecent(string api, TimeSpan window);
}
