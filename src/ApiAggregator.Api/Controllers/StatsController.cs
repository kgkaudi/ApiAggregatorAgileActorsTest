using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Controllers;

public sealed record BucketThresholds(double FastBelowMs, double AverageUpToMs);

public sealed record StatsResponse(DateTimeOffset GeneratedAt, BucketThresholds Thresholds, IReadOnlyList<ApiStatsSnapshot> Apis);

[ApiController]
[Authorize]
[Route("api/stats")]
public sealed class StatsController(IRequestStatsStore store, IOptions<StatsOptions> options, TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Per external API: total requests, failed requests, average response time, and the same
    /// grouped into fast / average / slow buckets. Only live calls to the external API are counted
    /// (cache hits do not call the API).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(StatsResponse), StatusCodes.Status200OK)]
    public ActionResult<StatsResponse> Get() =>
        new StatsResponse(
            clock.GetUtcNow(),
            new BucketThresholds(options.Value.FastThresholdMs, options.Value.AverageThresholdMs),
            store.GetSnapshots());
}
