using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/risk")]
public sealed class RiskController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(CancellationToken cancellationToken)
    {
        var summary = await readService.GetDashboardSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken cancellationToken) =>
        Ok(await readService.GetRiskScoreHistoryAsync(cancellationToken));

    [HttpGet("latest/attribution")]
    public async Task<IActionResult> GetLatestAttribution(CancellationToken cancellationToken)
    {
        var summary = await readService.GetDashboardSummaryAsync(cancellationToken);
        return Ok(new
        {
            summary.CurrentRiskScore,
            summary.RiskBand,
            Positive = summary.TopPositiveIndicators,
            Negative = summary.TopNegativeIndicators,
            Components = summary.CategoryStatuses
        });
    }
}
