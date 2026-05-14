using AiCapex.Application.Dashboard;
using AiCapex.Application.Alerts;
using AiCapex.Application.Scoring;
using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/manual-entry")]
public sealed class ManualEntryController(IManualEntryService manualEntryService, IRiskScoringService scoringService, IAlertGenerationService alertGenerationService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(ManualEntryRequest request, CancellationToken cancellationToken)
    {
        var signal = await manualEntryService.AddManualEntryAsync(request, cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(signal);
    }
}
