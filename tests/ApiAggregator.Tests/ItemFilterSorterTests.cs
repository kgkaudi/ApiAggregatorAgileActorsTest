using ApiAggregator.Api.Models;
using ApiAggregator.Api.Services;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

public class ItemFilterSorterTests
{
    private static readonly DateTimeOffset Jan = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly AggregatedItem[] Items =
    [
        Item("GitHub", "alpha", "Code", Jan.AddDays(10), 0.9),
        Item("HackerNews", "bravo", "News", Jan.AddDays(30), 0.5),
        Item("Wikipedia", "Charlie", "Reference", Jan.AddDays(20), 0.7),
        Item("Wikipedia", "delta", "Reference", null, 0.1)
    ];

    private static string[] Titles(AggregationQuery q) => ItemFilterSorter.Apply(Items, q).Select(i => i.Title).ToArray();

    [Fact]
    public void Filters_by_category_case_insensitively()
    {
        var result = Titles(new AggregationQuery { SearchTerm = "x", Category = "reference" });
        Assert.Equal(["Charlie", "delta"], result.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public void Filters_by_inclusive_date_range_and_excludes_undated_items()
    {
        var result = Titles(new AggregationQuery
        {
            SearchTerm = "x", From = Jan.AddDays(10), To = Jan.AddDays(20), SortBy = SortField.Date, Order = SortDirection.Asc
        });
        Assert.Equal(["alpha", "Charlie"], result);
    }

    [Fact]
    public void Sorts_by_relevance_descending_by_default()
    {
        Assert.Equal(["alpha", "Charlie", "bravo", "delta"], Titles(new AggregationQuery { SearchTerm = "x" }));
    }

    [Fact]
    public void Sorts_by_date_descending_with_undated_last()
    {
        var q = new AggregationQuery { SearchTerm = "x", SortBy = SortField.Date, Order = SortDirection.Desc };
        Assert.Equal(["bravo", "Charlie", "alpha", "delta"], Titles(q));
    }

    [Fact]
    public void Sorts_by_title_ascending_ignoring_case()
    {
        var q = new AggregationQuery { SearchTerm = "x", SortBy = SortField.Title, Order = SortDirection.Asc };
        Assert.Equal(["alpha", "bravo", "Charlie", "delta"], Titles(q));
    }

    [Fact]
    public void Sorts_by_source()
    {
        var q = new AggregationQuery { SearchTerm = "x", SortBy = SortField.Source, Order = SortDirection.Asc };
        Assert.Equal("GitHub", ItemFilterSorter.Apply(Items, q)[0].Source);
        Assert.Equal("Wikipedia", ItemFilterSorter.Apply(Items, q)[^1].Source);
    }
}
