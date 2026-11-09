using ApiAggregator.Api.Models;

namespace ApiAggregator.Api.Sources;

/// <summary>
/// One external API. To integrate a new API: implement this interface and register it
/// with <c>services.AddDataSource&lt;T&gt;(...)</c>. Nothing else needs to change.
/// Implementations should just call the API and map the result; caching, retries,
/// fallback and statistics are handled by the infrastructure around them.
/// </summary>
public interface IDataSource
{
    string Name { get; }
    string Category { get; }
    Task<IReadOnlyList<AggregatedItem>> FetchAsync(string searchTerm, CancellationToken cancellationToken);
}
