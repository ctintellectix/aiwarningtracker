using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(IDataSourceStatusService statusService) : ControllerBase
{
    [HttpGet("data-sources")]
    public async Task<IActionResult> GetDataSources(CancellationToken cancellationToken) =>
        Ok(await statusService.GetStatusAsync(cancellationToken));
}
