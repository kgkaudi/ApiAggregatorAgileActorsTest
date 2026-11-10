using ApiAggregator.Api.Models;
using ApiAggregator.Api.Services;
using ApiAggregator.Api.Sources;
using static ApiAggregator.Tests.TestHelpers;

namespace ApiAggregator.Tests;

public class AggregationServiceTests
{
    private static AggregationService Create(params IDataSource[] sources) =>
        new(sources, CreateFetcher().Fetcher);

    [Fact]
    public async Task Merges_items_from_all_sources_and_reports_each_source()
    {
        var service = Create(
            FakeSource.Returning("A", Item("A", "a1"), Item("A", "a2")),
            FakeSource.Returning("B", Item("B", "b1")));

        var result = await service.AggregateAsync(new AggregationQuery { SearchTerm = "x" }, default);

        Assert.Equal(3, result.TotalItems);
        Assert.Equal(2, result.Sources.Count);
        Assert.All(result.Sources, s => Assert.Equal(SourceStatus.Live, s.Status));
    }

    [Fact]
    public async Task Queries_all_sources_in_parallel()
    {
        // Each source only completes once ALL three have started. If they ran sequentially this would hang
        // and WaitAsync would time out, so passing proves the calls overlap.
        var started = 0;
        var allStarted = new TaskCompletionSource();

        FakeSource Barrier(string name) => new(name, async (_, _) =>
        {
            if (Interlocked.Increment(ref started) == 3) allStarted.TrySetResult();
            await allStarted.Task;
            return [Item(name, name)];
        });

        var service = Create(Barrier("A"), Barrier("B"), Barrier("C"));

        var result = await service
            .AggregateAsync(new AggregationQuery { SearchTerm = "x" }, default)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, result.TotalItems);
    }

    [Fact]
    public async Task One_failing_source_does_not_fail_the_request()
    {
        var service = Create(FakeSource.Returning("A", Item("A", "a1")), FakeSource.Failing("B"));

        var result = await service.AggregateAsync(new AggregationQuery { SearchTerm = "x" }, default);

        Assert.Single(result.Items);
        Assert.False(result.AllSourcesFailed);
        Assert.Equal(SourceStatus.Failed, result.Sources.Single(s => s.Source == "B").Status);
    }

    [Fact]
    public async Task Reports_when_all_sources_failed()
    {
        var service = Create(FakeSource.Failing("A"), FakeSource.Failing("B"));

        var result = await service.AggregateAsync(new AggregationQuery { SearchTerm = "x" }, default);

        Assert.True(result.AllSourcesFailed);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Only_queries_requested_sources()
    {
        var a = FakeSource.Returning("A", Item("A", "a1"));
        var b = FakeSource.Returning("B", Item("B", "b1"));
        var service = Create(a, b);

        var result = await service.AggregateAsync(new AggregationQuery
        {
            SearchTerm = "x",
            Sources = new HashSet<string>(["b"], StringComparer.OrdinalIgnoreCase)
        }, default);

        Assert.Equal(0, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Equal("b1", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task Applies_paging_after_sorting()
    {
        var items = Enumerable.Range(1, 5).Select(i => Item("A", $"t{i}", relevance: i / 10.0)).ToArray();
        var service = Create(FakeSource.Returning("A", items));

        var result = await service.AggregateAsync(new AggregationQuery { SearchTerm = "x", Page = 2, PageSize = 2 }, default);

        Assert.Equal(5, result.TotalItems);
        Assert.Equal(["t3", "t2"], result.Items.Select(i => i.Title));   // relevance desc: t5 t4 | t3 t2 | t1
    }
}
