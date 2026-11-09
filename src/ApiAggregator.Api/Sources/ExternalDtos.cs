using System.Text.Json.Serialization;

namespace ApiAggregator.Api.Sources;

// GitHub
internal sealed record GitHubSearchResponse([property: JsonPropertyName("items")] List<GitHubRepo>? Items);

internal sealed record GitHubRepo(
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("stargazers_count")] int StargazersCount,
    [property: JsonPropertyName("language")] string? Language);

// Hacker News (Algolia)
internal sealed record HackerNewsSearchResponse([property: JsonPropertyName("hits")] List<HackerNewsHit>? Hits);

internal sealed record HackerNewsHit(
    [property: JsonPropertyName("objectID")] string? ObjectId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("points")] int? Points,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);

// Wikipedia
internal sealed record WikipediaResponse([property: JsonPropertyName("query")] WikipediaQuery? Query);

internal sealed record WikipediaQuery([property: JsonPropertyName("search")] List<WikipediaHit>? Search);

internal sealed record WikipediaHit(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("snippet")] string? Snippet,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp);
