using AiCapex.Application.Alerts;
using AiCapex.Application.Scoring;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/scoring")]
public sealed class ScoringController(IRiskScoringService scoringService, IAlertGenerationService alertGenerationService) : ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        var score = await scoringService.RecalculateAsync(cancellationToken);
        var alerts = await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(new { score, alerts });
    }
}
