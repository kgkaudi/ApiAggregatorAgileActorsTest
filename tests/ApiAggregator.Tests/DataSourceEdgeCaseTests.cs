using System.Net;
using System.Text;
using System.Text.Json;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Sources;
using Microsoft.Extensions.Options;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

public class DataSourceEdgeCaseTests
{
    private static readonly IOptions<AggregationOptions> Opts = Options.Create(new AggregationOptions { MaxItemsPerSource = 5 });

    private static IDataSource[] AllSources(string json) =>
    [
        new GitHubDataSource(JsonClient(json, "https://api.github.com/"), Opts),
        new HackerNewsDataSource(JsonClient(json, "https://hn.algolia.com/api/v1/"), Opts),
        new WikipediaDataSource(JsonClient(json, "https://en.wikipedia.org/w/"), Opts)
    ];

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"items":null,"hits":null,"query":null}""")]
    [InlineData("""{"items":[],"hits":[],"query":{"search":[]}}""")]
    [InlineData("""{"query":{}}""")]
    [InlineData("""{"query":{"search":null}}""")]
    public async Task Empty_or_missing_collections_yield_no_items_instead_of_failing(string json)
    {
        foreach (var source in AllSources(json))
            Assert.Empty(await source.FetchAsync("x", default));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[1,2]")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    public async Task Malformed_json_throws_so_the_fetcher_can_treat_the_source_as_failed(string body)
    {
        foreach (var source in AllSources(body))
            await Assert.ThrowsAnyAsync<JsonException>(() => source.FetchAsync("x", default));
    }

    [Fact]
    public async Task GitHub_skips_repositories_without_a_name_and_ranks_the_rest()
    {
        const string json = """
        { "items": [
            { "full_name": null, "html_url": "https://github.com/x" },
            { "full_name": "   " },
            { "full_name": "a/b", "html_url": "https://github.com/a/b", "stargazers_count": 1 }
        ] }
        """;
        var source = new GitHubDataSource(JsonClient(json, "https://api.github.com/"), Opts);

        var item = Assert.Single(await source.FetchAsync("x", default));

        Assert.Equal("a/b", item.Title);
        Assert.Equal(1.0, item.Relevance);   // ranked among the valid items only
        Assert.Null(item.PublishedAt);       // missing date is allowed
        Assert.Equal("stars: 1", item.Description);
    }

    [Fact]
    public async Task HackerNews_skips_stories_without_a_title_and_tolerates_missing_fields()
    {
        const string json = """
        { "hits": [
            { "objectID": "1", "title": "" },
            { "objectID": "2", "title": "Real story", "url": "https://example.test/story" }
        ] }
        """;
        var source = new HackerNewsDataSource(JsonClient(json, "https://hn.algolia.com/api/v1/"), Opts);

        var item = Assert.Single(await source.FetchAsync("x", default));

        Assert.Equal("Real story", item.Title);
        Assert.Equal("https://example.test/story", item.Url);
        Assert.Null(item.PublishedAt);
        Assert.Null(item.Description);   // no author and no points: nothing to describe
    }

    [Fact]
    public async Task HackerNews_description_only_includes_the_fields_that_exist()
    {
        const string json = """
        { "hits": [
            { "objectID": "1", "title": "both",   "author": "bob", "points": 7 },
            { "objectID": "2", "title": "author", "author": "al" },
            { "objectID": "3", "title": "points", "points": 0 },
            { "objectID": "4", "title": "none" }
        ] }
        """;
        var source = new HackerNewsDataSource(JsonClient(json, "https://hn.algolia.com/api/v1/"), Opts);

        var descriptions = (await source.FetchAsync("x", default)).Select(i => i.Description).ToArray();

        string?[] expected = ["by bob · points: 7", "by al", "points: 0", null];
        Assert.Equal(expected.AsEnumerable(), descriptions.AsEnumerable());
    }

    [Fact]
    public async Task Wikipedia_skips_hits_without_a_title_and_tolerates_a_missing_snippet()
    {
        const string json = """
        { "query": { "search": [
            { "title": null, "snippet": "orphan" },
            { "title": "Café au lait", "snippet": null }
        ] } }
        """;
        var source = new WikipediaDataSource(JsonClient(json, "https://en.wikipedia.org/w/"), Opts);

        var item = Assert.Single(await source.FetchAsync("x", default));

        Assert.Equal("Café au lait", item.Title);
        Assert.Null(item.Description);
        Assert.Null(item.PublishedAt);
        Assert.Equal("https://en.wikipedia.org/wiki/" + Uri.EscapeDataString("Café_au_lait"), item.Url);
    }

    [Fact]
    public async Task Search_terms_with_special_characters_are_url_encoded_and_the_item_limit_is_sent()
    {
        const string term = "c# & .net";
        var cases = new (Func<HttpClient, IDataSource> Create, string BaseUrl, string LimitParameter)[]
        {
            (c => new GitHubDataSource(c, Opts), "https://api.github.com/", "per_page=5"),
            (c => new HackerNewsDataSource(c, Opts), "https://hn.algolia.com/api/v1/", "hitsPerPage=5"),
            (c => new WikipediaDataSource(c, Opts), "https://en.wikipedia.org/w/", "srlimit=5")
        };

        foreach (var (create, baseUrl, limitParameter) in cases)
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
            var source = create(new HttpClient(handler) { BaseAddress = new Uri(baseUrl) });

            await source.FetchAsync(term, default);

            var uri = Assert.Single(handler.Requests).AbsoluteUri;
            Assert.Contains(Uri.EscapeDataString(term), uri);
            Assert.DoesNotContain("c# &", uri);
            Assert.Contains(limitParameter, uri);
        }
    }
}
