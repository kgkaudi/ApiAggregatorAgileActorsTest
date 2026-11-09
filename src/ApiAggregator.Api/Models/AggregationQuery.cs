namespace ApiAggregator.Api.Models;

public enum SortField { Date, Relevance, Title, Source }

public enum SortDirection { Asc, Desc }

/// <summary>Domain-level query (already validated/parsed from the HTTP request).</summary>
public sealed record AggregationQuery
{
    public required string SearchTerm { get; init; }
    public string? Category { get; init; }
    public IReadOnlySet<string>? Sources { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public SortField SortBy { get; init; } = SortField.Relevance;
    public SortDirection Order { get; init; } = SortDirection.Desc;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
