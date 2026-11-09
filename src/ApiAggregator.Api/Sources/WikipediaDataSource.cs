using System.Net;
using System.Text.RegularExpressions;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Models;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Sources;

public sealed partial class WikipediaDataSource(HttpClient http, IOptions<AggregationOptions> options) : IDataSource
{
    public string Name => "Wikipedia";
    public string Category => "Reference";

    public async Task<IReadOnlyList<AggregatedItem>> FetchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var url = $"api.php?action=query&list=search&format=json&srsearch={Uri.EscapeDataString(searchTerm)}&srlimit={options.Value.MaxItemsPerSource}";
        var response = await http.GetFromJsonAsync<WikipediaResponse>(url, cancellationToken);

        var hits = (response?.Query?.Search ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h.Title))
            .ToList();

        return hits.Select((h, i) => new AggregatedItem
        {
            Source = Name,
            Category = Category,
            Title = h.Title!,
            Url = $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(h.Title!.Replace(" ", "_"))}",
            Description = StripHtml(h.Snippet),
            PublishedAt = h.Timestamp,
            Relevance = RelevanceScore.FromRank(i, hits.Count)
        }).ToList();
    }

    private static string? StripHtml(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : WebUtility.HtmlDecode(HtmlTag().Replace(html, string.Empty));

    [GeneratedRegex("<.*?>")]
    private static partial Regex HtmlTag();
}
