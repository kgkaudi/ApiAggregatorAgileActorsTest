using ApiAggregator.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Statistics;

/// <summary>Background service that periodically runs the analyzer and logs anomalies.</summary>
public sealed class PerformanceAnomalyWorker(
    PerformanceAnomalyAnalyzer analyzer,
    IOptions<AnomalyDetectionOptions> options,
    ILogger<PerformanceAnomalyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        using var timer = new PeriodicTimer(o.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    foreach (var a in analyzer.Analyze())
                    {
                        logger.LogWarning(
                            "Performance anomaly for {Api}: average over the last {Window} is {Recent:F0} ms " +
                            "vs overall average {Overall:F0} ms (+{Increase:F0}%, {Samples} samples)",
                            a.Api, o.Window, a.RecentAverageMs, a.OverallAverageMs,
                            (a.RecentAverageMs / a.OverallAverageMs - 1) * 100, a.RecentSampleCount);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed analysis run must never kill the worker.
                    logger.LogError(ex, "Anomaly analysis failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
