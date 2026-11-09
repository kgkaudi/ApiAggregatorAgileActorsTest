using ApiAggregator.Api.Models;
using ApiAggregator.Api.Sources;

namespace ApiAggregator.Api.Services;

/// <summary>Gets data for one source, applying caching, fallback and statistics. Never throws for source failures.</summary>
public interface ISourceFetcher
{
    Task<SourceOutcome> GetAsync(IDataSource source, string searchTerm, CancellationToken cancellationToken);
}
