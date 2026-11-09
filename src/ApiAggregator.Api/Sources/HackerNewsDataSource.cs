using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Models;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Sources;

public sealed class HackerNewsDataSource(HttpClient http, IOptions<AggregationOptions> options) : IDataSource
{
    public string Name => "HackerNews";
    public string Category => "News";

    public async Task<IReadOnlyList<AggregatedItem>> FetchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var url = $"search?query={Uri.EscapeDataString(searchTerm)}&tags=story&hitsPerPage={options.Value.MaxItemsPerSource}";
        var response = await http.GetFromJsonAsync<HackerNewsSearchResponse>(url, cancellationToken);

        var hits = (response?.Hits ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h.Title))
            .ToList();

        return hits.Select((h, i) => new AggregatedItem
        {
            Source = Name,
            Category = Category,
            Title = h.Title!,
            // "Ask HN" style posts have no external URL: link to the discussion instead.
            Url = string.IsNullOrWhiteSpace(h.Url) ? $"https://news.ycombinator.com/item?id={h.ObjectId}" : h.Url,
            Description = $"by {h.Author} · points: {h.Points ?? 0}",
            PublishedAt = h.CreatedAt,
            Relevance = RelevanceScore.FromRank(i, hits.Count)
        }).ToList();
    }
}
