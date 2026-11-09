using ApiAggregator.Api.Models;

namespace ApiAggregator.Api.Services;

public interface IAggregationService
{
    IReadOnlyCollection<string> SourceNames { get; }
    Task<AggregationResult> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken);
}
