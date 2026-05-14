using AiCapex.Application.Dashboard;
using AiCapex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/sources")]
public sealed class SourcesController(AiCapexDbContext db) : ControllerBase
{
    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments(CancellationToken cancellationToken)
    {
        var sourceDocuments = await db.SourceDocuments
            .Include(x => x.Company)
            .ToListAsync(cancellationToken);

        var documents = sourceDocuments
            .OrderByDescending(x => x.RetrievedAtUtc ?? x.PublishedAtUtc ?? DateTimeOffset.MinValue)
            .Select(x => new SourceDocumentDto(
                x.Company == null ? null : x.Company.Ticker,
                x.SourceType,
                x.Title,
                x.Url,
                x.Summary,
                x.PublishedDate))
            .ToList();

        return Ok(documents);
    }
}
