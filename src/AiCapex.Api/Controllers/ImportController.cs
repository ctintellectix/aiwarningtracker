using AiCapex.Application.Dashboard;
using AiCapex.Application.Alerts;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Scoring;
using AiCapex.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/import")]
public sealed class ImportController(
    ISecCompanyFactImporter secImporter,
    IRssImportService rssImporter,
    ITranscriptImportService transcriptImporter,
    IRiskScoringService scoringService,
    IAlertGenerationService alertGenerationService,
    AiCapexDbContext db) : ControllerBase
{
    [HttpPost("sec/{ticker}")]
    public async Task<IActionResult> ImportSec(string ticker, CancellationToken cancellationToken)
    {
        var result = await secImporter.ImportAsync(ticker, cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("sec/all")]
    public async Task<IActionResult> ImportSecAll(CancellationToken cancellationToken)
    {
        var tickers = await GetTrackedTickers(cancellationToken);
        var results = new List<BulkImportItemDto>();
        foreach (var ticker in tickers)
        {
            try
            {
                var result = await secImporter.ImportAsync(ticker, cancellationToken);
                results.Add(new BulkImportItemDto(ticker, result.FactsImported > 0 || result.MetricsImported > 0, result.Message, result.FactsImported, result.MetricsImported));
            }
            catch (Exception ex)
            {
                results.Add(new BulkImportItemDto(ticker, false, ex.Message, 0, 0));
            }
        }

        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(BulkImportSummary.Create("SEC EDGAR", results));
    }

    [HttpPost("rss")]
    public async Task<IActionResult> ImportRss(CancellationToken cancellationToken)
    {
        var result = await rssImporter.ImportAsync(cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("all")]
    public async Task<IActionResult> ImportAll(CancellationToken cancellationToken)
    {
        var tickers = await GetTrackedTickers(cancellationToken);
        var secResults = new List<BulkImportItemDto>();

        foreach (var ticker in tickers)
        {
            try
            {
                var result = await secImporter.ImportAsync(ticker, cancellationToken);
                secResults.Add(new BulkImportItemDto(ticker, result.FactsImported > 0 || result.MetricsImported > 0, result.Message, result.FactsImported, result.MetricsImported));
            }
            catch (Exception ex)
            {
                secResults.Add(new BulkImportItemDto(ticker, false, ex.Message, 0, 0));
            }
        }

        var rss = await rssImporter.ImportAsync(cancellationToken);
        var transcripts = await transcriptImporter.ImportRecentQuartersAsync(tickers, 4, cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(new
        {
            sec = BulkImportSummary.Create("SEC EDGAR", secResults),
            transcripts,
            rss
        });
    }

    private async Task<IReadOnlyList<string>> GetTrackedTickers(CancellationToken cancellationToken) =>
        await db.Companies.OrderBy(x => x.Ticker).Select(x => x.Ticker).ToListAsync(cancellationToken);
}
