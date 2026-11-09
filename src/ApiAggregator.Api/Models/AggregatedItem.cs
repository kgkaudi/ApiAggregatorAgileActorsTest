namespace ApiAggregator.Api.Models;

/// <summary>Unified item shape that every external API is mapped into.</summary>
public sealed record AggregatedItem
{
    public required string Source { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public required string Category { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>0..1 score based on the item position in its source own relevance ranking.</summary>
    public double Relevance { get; init; }
}
