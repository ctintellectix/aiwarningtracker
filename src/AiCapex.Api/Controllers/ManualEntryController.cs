using AiCapex.Application.Dashboard;
using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/manual-entry")]
public sealed class ManualEntryController(IManualEntryService manualEntryService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(ManualEntryRequest request, CancellationToken cancellationToken) =>
        Ok(await manualEntryService.AddManualEntryAsync(request, cancellationToken));
}
