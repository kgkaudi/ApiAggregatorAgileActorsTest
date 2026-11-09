using ApiAggregator.Api.Models;
using ApiAggregator.Api.Sources;

namespace ApiAggregator.Api.Services;

public sealed class AggregationService : IAggregationService
{
    private readonly IReadOnlyList<IDataSource> _sources;
    private readonly ISourceFetcher _fetcher;

    public AggregationService(IEnumerable<IDataSource> sources, ISourceFetcher fetcher)
    {
        _sources = sources.ToList();
        _fetcher = fetcher;
    }

    public IReadOnlyCollection<string> SourceNames => _sources.Select(s => s.Name).ToList();

    public async Task<AggregationResult> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var selected = _sources
            .Where(s => query.Sources is not { Count: > 0 } || query.Sources.Contains(s.Name))
            .ToList();

        // All sources are queried in parallel; total latency ~= the slowest source, not the sum.
        // The fetcher never throws for source failures, so one bad API cannot fail the whole request.
        var outcomes = await Task.WhenAll(selected.Select(s => _fetcher.GetAsync(s, query.SearchTerm, cancellationToken)));

        var processed = ItemFilterSorter.Apply(outcomes.SelectMany(o => o.Items), query);

        var page = processed
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new AggregationResult(
            query.SearchTerm,
            processed.Count,
            query.Page,
            query.PageSize,
            page,
            outcomes.Select(o => o.Report).ToList());
    }
}
