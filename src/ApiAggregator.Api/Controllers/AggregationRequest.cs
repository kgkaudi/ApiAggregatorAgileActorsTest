using System.ComponentModel.DataAnnotations;
using ApiAggregator.Api.Models;

namespace ApiAggregator.Api.Controllers;

/// <summary>Query-string parameters of <c>GET /api/aggregation</c>.</summary>
public sealed class AggregationRequest
{
    /// <summary>Search term sent to every source (required, 2-100 chars).</summary>
    [Required, MinLength(2), MaxLength(100)]
    public string Query { get; set; } = string.Empty;

    /// <summary>Only return items of this category: Code, News or Reference.</summary>
    public string? Category { get; set; }

    /// <summary>Comma-separated list of sources to query, e.g. <c>GitHub,Wikipedia</c>. Default: all.</summary>
    public string? Sources { get; set; }

    /// <summary>Only items published on/after this instant (ISO 8601, e.g. 2024-01-01T00:00:00Z).</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Only items published on/before this instant (ISO 8601).</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Date, Relevance, Title or Source. Default: Relevance.</summary>
    public SortField SortBy { get; set; } = SortField.Relevance;

    /// <summary>Asc or Desc. Default: Desc.</summary>
    public SortDirection Order { get; set; } = SortDirection.Desc;

    [Range(1, 10_000)] public int Page { get; set; } = 1;

    [Range(1, 100)] public int PageSize { get; set; } = 20;
}
