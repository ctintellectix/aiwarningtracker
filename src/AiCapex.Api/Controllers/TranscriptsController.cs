using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/transcripts")]
public sealed class TranscriptsController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet("signals")]
    public async Task<IActionResult> GetSignals(CancellationToken cancellationToken) =>
        Ok(await readService.GetTranscriptSignalsAsync(cancellationToken));
}
