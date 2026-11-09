using System.Text.Json.Serialization;

namespace ApiAggregator.Api.Models;

public enum SourceStatus
{
    /// <summary>Fetched from the external API during this request.</summary>
    Live,
    /// <summary>Served from the fresh cache.</summary>
    Cached,
    /// <summary>External API failed; last known good data was served instead (fallback).</summary>
    Stale,
    /// <summary>External API failed and no fallback data was available.</summary>
    Failed
}

public sealed record SourceReport(
    string Source,
    SourceStatus Status,
    int ItemCount,
    long DurationMs,
    DateTimeOffset? DataRetrievedAt,
    string? Error);

public sealed record SourceOutcome(SourceReport Report, IReadOnlyList<AggregatedItem> Items);

public sealed record AggregationResult(
    string Query,
    int TotalItems,
    int Page,
    int PageSize,
    IReadOnlyList<AggregatedItem> Items,
    IReadOnlyList<SourceReport> Sources)
{
    [JsonIgnore]
    public bool AllSourcesFailed => Sources.Count > 0 && Sources.All(s => s.Status == SourceStatus.Failed);
}
