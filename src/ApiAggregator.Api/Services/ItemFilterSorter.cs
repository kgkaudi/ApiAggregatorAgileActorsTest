using ApiAggregator.Api.Models;

namespace ApiAggregator.Api.Services;

/// <summary>Filtering and sorting of the merged result set. Pure and side-effect free.</summary>
public static class ItemFilterSorter
{
    public static IReadOnlyList<AggregatedItem> Apply(IEnumerable<AggregatedItem> items, AggregationQuery query)
    {
        var filtered = items;

        if (!string.IsNullOrWhiteSpace(query.Category))
            filtered = filtered.Where(i => string.Equals(i.Category, query.Category, StringComparison.OrdinalIgnoreCase));

        // Date filters are inclusive; items without a date cannot satisfy a date filter.
        if (query.From is { } from)
            filtered = filtered.Where(i => i.PublishedAt is { } d && d >= from);
        if (query.To is { } to)
            filtered = filtered.Where(i => i.PublishedAt is { } d && d <= to);

        var asc = query.Order == SortDirection.Asc;

        IOrderedEnumerable<AggregatedItem> ordered = query.SortBy switch
        {
            SortField.Date => OrderBy(filtered, i => i.PublishedAt ?? DateTimeOffset.MinValue, asc),
            SortField.Title => OrderBy(filtered, i => i.Title, asc, StringComparer.OrdinalIgnoreCase),
            SortField.Source => OrderBy(filtered, i => i.Source, asc, StringComparer.OrdinalIgnoreCase),
            _ => OrderBy(filtered, i => i.Relevance, asc)
        };

        // Tie-breakers keep the order deterministic (important for paging).
        return ordered
            .ThenBy(i => i.Source, StringComparer.Ordinal)
            .ThenBy(i => i.Title, StringComparer.Ordinal)
            .ToList();
    }

    private static IOrderedEnumerable<AggregatedItem> OrderBy<TKey>(
        IEnumerable<AggregatedItem> items, Func<AggregatedItem, TKey> key, bool ascending, IComparer<TKey>? comparer = null) =>
        ascending ? items.OrderBy(key, comparer) : items.OrderByDescending(key, comparer);
}
