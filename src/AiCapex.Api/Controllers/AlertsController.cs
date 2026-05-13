using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAlerts(CancellationToken cancellationToken) =>
        Ok(await readService.GetAlertsAsync(cancellationToken));
}
