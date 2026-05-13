using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken) =>
        Ok(await readService.GetDashboardSummaryAsync(cancellationToken));
}
