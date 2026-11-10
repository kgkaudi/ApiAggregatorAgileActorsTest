using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiAggregator.Api.Controllers;
using ApiAggregator.Api.Sources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

/// <summary>
/// Runs the real ASP.NET pipeline (auth, model binding, controller, services, cache, stats) in memory,
/// with the three external APIs replaced by fakes, so no network is used.
/// Every test builds its own host, so caches and statistics never leak between tests.
/// </summary>
public class ApiEndToEndTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Jan = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task<HttpClient> ClientWithAsync(params IDataSource[] sources)
    {
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataSource>();
            foreach (var source in sources) services.AddSingleton<IDataSource>(source);
        })).CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { username = "demo", password = "demo-password" });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string[] Titles(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();

    [Fact]
    public async Task Returns_200_with_merged_items_and_a_report_per_source()
    {
        var client = await ClientWithAsync(
            FakeSource.Returning("FakeA", Item("FakeA", "a1"), Item("FakeA", "a2")),
            FakeSource.Returning("FakeB", Item("FakeB", "b1")));

        var response = await client.GetAsync("/api/aggregation?query=dotnet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("dotnet", json.GetProperty("query").GetString());
        Assert.Equal(3, json.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, json.GetProperty("items").GetArrayLength());

        var sources = json.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(2, sources.Count);
        Assert.All(sources, s => Assert.Equal("Live", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Filters_by_category_and_sorts_by_title()
    {
        var client = await ClientWithAsync(
            FakeSource.Returning("FakeA", Item("FakeA", "alpha", "Code"), Item("FakeA", "zeta", "News")),
            FakeSource.Returning("FakeB", Item("FakeB", "beta", "News")));

        var response = await client.GetAsync("/api/aggregation?query=dotnet&category=news&sortBy=Title&order=Asc");

        Assert.Equal(["beta", "zeta"], Titles(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Filters_by_date_range()
    {
        var client = await ClientWithAsync(FakeSource.Returning("FakeA",
            Item("FakeA", "early", date: Jan),
            Item("FakeA", "middle", date: Jan.AddDays(19)),
            Item("FakeA", "late", date: Jan.AddDays(29)),
            Item("FakeA", "undated")));

        var response = await client.GetAsync("/api/aggregation?query=dotnet&from=2024-01-15T00:00:00Z&to=2024-01-25T00:00:00Z");

        Assert.Equal(["middle"], Titles(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task One_failing_source_still_returns_200_with_the_others()
    {
        var client = await ClientWithAsync(
            FakeSource.Returning("FakeA", Item("FakeA", "a1")),
            FakeSource.Failing("FakeB"));

        var response = await client.GetAsync("/api/aggregation?query=dotnet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(["a1"], Titles(json));

        var failed = json.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("source").GetString() == "FakeB");
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Contains("boom", failed.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Returns_503_with_per_source_errors_when_every_source_fails()
    {
        var client = await ClientWithAsync(FakeSource.Failing("FakeA"), FakeSource.Failing("FakeB"));

        var response = await client.GetAsync("/api/aggregation?query=dotnet");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(0, json.GetProperty("totalItems").GetInt32());
        Assert.All(json.GetProperty("sources").EnumerateArray(), s => Assert.Equal("Failed", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Paging_returns_the_requested_slice_and_an_empty_page_past_the_end()
    {
        var client = await ClientWithAsync(FakeSource.Returning("FakeA",
            Item("FakeA", "t1", relevance: 0.9), Item("FakeA", "t2", relevance: 0.8), Item("FakeA", "t3", relevance: 0.7)));

        var second = await ReadJsonAsync(await client.GetAsync("/api/aggregation?query=dotnet&page=2&pageSize=2"));
        Assert.Equal(["t3"], Titles(second));
        Assert.Equal(3, second.GetProperty("totalItems").GetInt32());

        var beyond = await client.GetAsync("/api/aggregation?query=dotnet&page=5&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        var json = await ReadJsonAsync(beyond);
        Assert.Empty(Titles(json));
        Assert.Equal(3, json.GetProperty("totalItems").GetInt32());
        Assert.Equal(5, json.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task A_category_with_no_matches_returns_an_empty_result_but_still_reports_sources()
    {
        var client = await ClientWithAsync(FakeSource.Returning("FakeA", Item("FakeA", "a1", "Code")));

        var response = await client.GetAsync("/api/aggregation?query=dotnet&category=Reference");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(0, json.GetProperty("totalItems").GetInt32());
        Assert.Single(json.GetProperty("sources").EnumerateArray().ToList());
    }

    [Theory]
    [InlineData("query=dotnet&sortBy=Banana")]
    [InlineData("query=dotnet&order=Sideways")]
    [InlineData("query=dotnet&page=0")]
    [InlineData("query=dotnet&page=-1")]
    [InlineData("query=dotnet&pageSize=0")]
    [InlineData("query=dotnet&pageSize=101")]
    [InlineData("query=dotnet&from=2025-01-01T00:00:00Z&to=2024-01-01T00:00:00Z")]
    [InlineData("query=dotnet&from=not-a-date")]
    [InlineData("query=a")]
    [InlineData("query=")]
    [InlineData("query=%20%20%20")]
    public async Task Invalid_requests_return_400_and_never_reach_a_source(string queryString)
    {
        var source = FakeSource.Returning("FakeA", Item("FakeA", "a1"));
        var client = await ClientWithAsync(source);

        var response = await client.GetAsync("/api/aggregation?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_search_term_longer_than_100_characters_is_rejected()
    {
        var source = FakeSource.Returning("FakeA", Item("FakeA", "a1"));
        var client = await ClientWithAsync(source);

        var response = await client.GetAsync("/api/aggregation?query=" + new string('x', 101));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Statistics_count_live_calls_only_and_failed_calls_are_retried()
    {
        var healthy = FakeSource.Returning("FakeA", Item("FakeA", "a1"));
        var failing = FakeSource.Failing("FakeB");
        var client = await ClientWithAsync(healthy, failing);

        await client.GetAsync("/api/aggregation?query=dotnet");
        await client.GetAsync("/api/aggregation?query=dotnet");   // FakeA is cached, FakeB failed so it is called again

        var stats = await ReadJsonAsync(await client.GetAsync("/api/stats"));
        var apis = stats.GetProperty("apis").EnumerateArray().ToDictionary(a => a.GetProperty("api").GetString()!);

        Assert.Equal(1, apis["FakeA"].GetProperty("totalRequests").GetInt64());
        Assert.Equal(0, apis["FakeA"].GetProperty("failedRequests").GetInt64());
        Assert.Equal(2, apis["FakeB"].GetProperty("totalRequests").GetInt64());
        Assert.Equal(2, apis["FakeB"].GetProperty("failedRequests").GetInt64());
        Assert.Equal(1, healthy.Calls);
        Assert.Equal(2, failing.Calls);
    }

    [Fact]
    public async Task Ten_simultaneous_identical_requests_cause_a_single_upstream_call()
    {
        var slow = new FakeSource("FakeA", async (_, _) =>
        {
            await Task.Delay(200);
            return [Item("FakeA", "a1")];
        });
        var client = await ClientWithAsync(slow);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.GetAsync("/api/aggregation?query=dotnet")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, slow.Calls);
    }
}
