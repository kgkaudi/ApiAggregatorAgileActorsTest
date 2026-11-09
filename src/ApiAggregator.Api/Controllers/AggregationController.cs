using ApiAggregator.Api.Models;
using ApiAggregator.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiAggregator.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/aggregation")]
public sealed class AggregationController(IAggregationService service) : ControllerBase
{
    /// <summary>Aggregates data from all (or selected) external APIs, then filters, sorts and pages it.</summary>
    /// <response code="200">Aggregated data. Check <c>sources</c> for per-API status (Live, Cached, Stale, Failed).</response>
    /// <response code="400">Invalid parameters.</response>
    /// <response code="503">Every external API failed and no fallback data was available.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AggregationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AggregationResult), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get([FromQuery] AggregationRequest request, CancellationToken cancellationToken)
    {
        HashSet<string>? sources = null;
        if (!string.IsNullOrWhiteSpace(request.Sources))
        {
            sources = request.Sources
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unknown = sources.Where(s => !service.SourceNames.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Unknown source",
                    detail: $"Unknown source(s): {string.Join(", ", unknown)}. Available: {string.Join(", ", service.SourceNames)}.");
        }

        if (request.From > request.To)
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid date range",
                detail: "'from' must not be later than 'to'.");

        var result = await service.AggregateAsync(new AggregationQuery
        {
            SearchTerm = request.Query.Trim(),
            Category = request.Category,
            Sources = sources,
            From = request.From,
            To = request.To,
            SortBy = request.SortBy,
            Order = request.Order,
            Page = request.Page,
            PageSize = request.PageSize
        }, cancellationToken);

        return result.AllSourcesFailed ? StatusCode(StatusCodes.Status503ServiceUnavailable, result) : Ok(result);
    }
}
