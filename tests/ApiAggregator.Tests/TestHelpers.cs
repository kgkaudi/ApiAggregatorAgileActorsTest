using System.Net;
using System.Text;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Models;
using ApiAggregator.Api.Services;
using ApiAggregator.Api.Sources;
using ApiAggregator.Api.Statistics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Tests;

internal static class TestHelpers
{
    public static AggregatedItem Item(
        string source = "Src", string title = "Title", string category = "Code",
        DateTimeOffset? date = null, double relevance = 0.5) =>
        new() { Source = source, Title = title, Category = category, PublishedAt = date, Relevance = relevance };

    public static (SourceFetcher Fetcher, InMemoryRequestStatsStore Stats) CreateFetcher(TimeSpan? freshDuration = null)
    {
        var stats = new InMemoryRequestStatsStore(Options.Create(new StatsOptions()), TimeProvider.System);
        var fetcher = new SourceFetcher(
            new MemoryCache(new MemoryCacheOptions()),
            stats,
            Options.Create(new AggregationOptions
            {
                CacheDuration = freshDuration ?? TimeSpan.FromMinutes(5),
                FallbackCacheDuration = TimeSpan.FromHours(1),
                SourceTimeout = TimeSpan.FromSeconds(5)
            }),
            TimeProvider.System,
            NullLogger<SourceFetcher>.Instance);
        return (fetcher, stats);
    }

    public static HttpClient JsonClient(string json, string baseUrl, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }))
        { BaseAddress = new Uri(baseUrl) };
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

/// <summary>Configurable in-memory IDataSource that counts calls.</summary>
internal sealed class FakeSource(
    string name,
    Func<string, CancellationToken, Task<IReadOnlyList<AggregatedItem>>> fetch,
    string category = "Code") : IDataSource
{
    public int Calls;

    public string Name => name;
    public string Category => category;

    public Task<IReadOnlyList<AggregatedItem>> FetchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return fetch(searchTerm, cancellationToken);
    }

    public static FakeSource Returning(string name, params AggregatedItem[] items) =>
        new(name, (_, _) => Task.FromResult<IReadOnlyList<AggregatedItem>>(items));

    public static FakeSource Failing(string name) =>
        new(name, (_, _) => throw new HttpRequestException("boom"));
}
