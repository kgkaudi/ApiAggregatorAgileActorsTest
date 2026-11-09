using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Models;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Sources;

public sealed class GitHubDataSource(HttpClient http, IOptions<AggregationOptions> options) : IDataSource
{
    public string Name => "GitHub";
    public string Category => "Code";

    public async Task<IReadOnlyList<AggregatedItem>> FetchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var url = $"search/repositories?q={Uri.EscapeDataString(searchTerm)}&per_page={options.Value.MaxItemsPerSource}";
        var response = await http.GetFromJsonAsync<GitHubSearchResponse>(url, cancellationToken);

        var repos = (response?.Items ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.FullName))
            .ToList();

        return repos.Select((r, i) => new AggregatedItem
        {
            Source = Name,
            Category = Category,
            Title = r.FullName!,
            Url = r.HtmlUrl,
            Description = string.Join(" · ", new[] { r.Description, $"stars: {r.StargazersCount}", r.Language }
                .Where(s => !string.IsNullOrWhiteSpace(s))),
            PublishedAt = r.UpdatedAt,
            Relevance = RelevanceScore.FromRank(i, repos.Count)
        }).ToList();
    }
}
