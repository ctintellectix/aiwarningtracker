using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/risk-scores")]
public sealed class RiskScoresController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken cancellationToken) =>
        Ok(await readService.GetRiskScoreHistoryAsync(cancellationToken));
}
