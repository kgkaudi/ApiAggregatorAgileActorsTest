using System.Collections.Concurrent;
using ApiAggregator.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Statistics;

/// <summary>
/// In-memory statistics. A <see cref="ConcurrentDictionary{TKey,TValue}"/> maps API name to its
/// own <c>ApiStats</c>; each <c>ApiStats</c> guards its (very short) critical section with a lock,
/// so different APIs never contend with each other and each API counters stay consistent
/// (total, bucket counts and sums are always updated together).
/// </summary>
public sealed class InMemoryRequestStatsStore : IRequestStatsStore
{
    private readonly ConcurrentDictionary<string, ApiStats> _apis = new(StringComparer.OrdinalIgnoreCase);
    private readonly StatsOptions _options;
    private readonly TimeProvider _clock;

    public InMemoryRequestStatsStore(IOptions<StatsOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public void Record(string api, TimeSpan duration, bool success)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(api);
        _apis.GetOrAdd(api, _ => new ApiStats(_options, _clock)).Record(duration.TotalMilliseconds, success);
    }

    public IReadOnlyList<ApiStatsSnapshot> GetSnapshots() =>
        _apis.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
             .Select(kv => kv.Value.Snapshot(kv.Key))
             .ToList();

    public RecentStats GetRecent(string api, TimeSpan window) =>
        _apis.TryGetValue(api, out var stats) ? stats.Recent(window) : new RecentStats(0, 0);

    private readonly record struct Sample(DateTimeOffset At, double Ms);

    private sealed class ApiStats(StatsOptions options, TimeProvider clock)
    {
        private const int Fast = 0, Average = 1, Slow = 2;

        private readonly object _gate = new();
        private readonly long[] _bucketCounts = new long[3];
        private readonly double[] _bucketSumsMs = new double[3];
        private readonly Queue<Sample> _samples = new();
        private long _total, _failed;
        private double _totalMs;

        public void Record(double ms, bool success)
        {
            var bucket = Classify(ms);
            var now = clock.GetUtcNow();

            lock (_gate)
            {
                _total++;
                if (!success) _failed++;
                _totalMs += ms;
                _bucketCounts[bucket]++;
                _bucketSumsMs[bucket] += ms;

                _samples.Enqueue(new Sample(now, ms));
                Prune(now);
            }
        }

        public ApiStatsSnapshot Snapshot(string api)
        {
            lock (_gate)
            {
                return new ApiStatsSnapshot(
                    api, _total, _failed, Avg(_totalMs, _total),
                    new PerformanceBuckets(Bucket(Fast), Bucket(Average), Bucket(Slow)));
            }
        }

        public RecentStats Recent(TimeSpan window)
        {
            var now = clock.GetUtcNow();
            var cutoff = now - window;

            lock (_gate)
            {
                Prune(now);
                var count = 0;
                var sum = 0.0;
                foreach (var s in _samples)
                {
                    if (s.At < cutoff) continue;
                    count++;
                    sum += s.Ms;
                }
                return new RecentStats(count, count == 0 ? 0 : Math.Round(sum / count, 2));
            }
        }

        // Must be called while holding _gate.
        private void Prune(DateTimeOffset now)
        {
            var cutoff = now - options.SampleRetention;
            while (_samples.Count > 0 && (_samples.Count > options.MaxSamplesPerApi || _samples.Peek().At < cutoff))
                _samples.Dequeue();
        }

        private int Classify(double ms) =>
            ms < options.FastThresholdMs ? Fast :
            ms <= options.AverageThresholdMs ? Average : Slow;

        private BucketStats Bucket(int i) => new(_bucketCounts[i], Avg(_bucketSumsMs[i], _bucketCounts[i]));

        private static double Avg(double sum, long n) => n == 0 ? 0 : Math.Round(sum / n, 2);
    }
}
