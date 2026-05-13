using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/indicators")]
public sealed class IndicatorsController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet("trends")]
    public async Task<IActionResult> GetTrends(CancellationToken cancellationToken) =>
        Ok(await readService.GetIndicatorTrendsAsync(cancellationToken));
}
