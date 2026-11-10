using System.Net;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Sources;
using Microsoft.Extensions.Options;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

public class DataSourceTests
{
    private static readonly IOptions<AggregationOptions> Opts = Options.Create(new AggregationOptions { MaxItemsPerSource = 5 });

    [Fact]
    public async Task GitHub_maps_repositories()
    {
        const string json = """
        { "items": [
            { "full_name": "dotnet/runtime", "html_url": "https://github.com/dotnet/runtime",
              "description": "The runtime", "updated_at": "2024-05-01T10:00:00Z", "stargazers_count": 100, "language": "C#" },
            { "full_name": "second/repo", "html_url": "https://github.com/second/repo",
              "description": null, "updated_at": "2024-04-01T10:00:00Z", "stargazers_count": 1, "language": null }
        ] }
        """;
        var source = new GitHubDataSource(JsonClient(json, "https://api.github.com/"), Opts);

        var items = await source.FetchAsync("dotnet", default);

        Assert.Equal(2, items.Count);
        Assert.Equal("dotnet/runtime", items[0].Title);
        Assert.Equal("Code", items[0].Category);
        Assert.Equal(1.0, items[0].Relevance);
        Assert.Equal(0.5, items[1].Relevance);
        Assert.Contains("stars: 100", items[0].Description);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero), items[0].PublishedAt);
    }

    [Fact]
    public async Task HackerNews_falls_back_to_discussion_url_when_story_has_no_link()
    {
        const string json = """
        { "hits": [
            { "objectID": "42", "title": "Ask HN: Anything?", "url": null, "author": "bob", "points": 7, "created_at": "2024-03-01T00:00:00Z" },
            { "objectID": "43", "title": null, "url": "https://x.test", "author": "al", "points": 1, "created_at": "2024-03-01T00:00:00Z" }
        ] }
        """;
        var source = new HackerNewsDataSource(JsonClient(json, "https://hn.algolia.com/api/v1/"), Opts);

        var items = await source.FetchAsync("anything", default);

        var item = Assert.Single(items);   // the hit without a title is skipped
        Assert.Equal("https://news.ycombinator.com/item?id=42", item.Url);
        Assert.Equal("News", item.Category);
    }

    [Fact]
    public async Task Wikipedia_strips_html_from_snippets()
    {
        const string json = """
        { "query": { "search": [
            { "title": "ASP.NET Core", "snippet": "A <span class=\"searchmatch\">web</span> framework &amp; more", "timestamp": "2024-02-02T00:00:00Z" }
        ] } }
        """;
        var source = new WikipediaDataSource(JsonClient(json, "https://en.wikipedia.org/w/"), Opts);

        var item = Assert.Single(await source.FetchAsync("asp", default));

        Assert.Equal("A web framework & more", item.Description);
        Assert.Equal("https://en.wikipedia.org/wiki/ASP.NET_Core", item.Url);
        Assert.Equal("Reference", item.Category);
    }

    [Fact]
    public async Task Non_success_status_throws_so_the_fetcher_can_apply_fallback()
    {
        var source = new GitHubDataSource(JsonClient("{}", "https://api.github.com/", HttpStatusCode.ServiceUnavailable), Opts);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync("x", default));
    }
}
