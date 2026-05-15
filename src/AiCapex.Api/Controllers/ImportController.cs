using AiCapex.Application.Dashboard;
using AiCapex.Application.Alerts;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Api.Services;
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
    AiCapexDbContext db,
    IImportJobService importJobs,
    IServiceScopeFactory scopeFactory) : ControllerBase
{
    [HttpPost("sec/{ticker}")]
    public async Task<IActionResult> ImportSec(string ticker, CancellationToken cancellationToken)
    {
        var result = await secImporter.ImportAsync(ticker, cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("jobs/sec/{ticker}")]
    public IActionResult StartSecImportJob(string ticker)
    {
        var normalizedTicker = ticker.ToUpperInvariant();
        var job = importJobs.Start($"SEC EDGAR {normalizedTicker}", async (progress, cancellationToken) =>
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<ISecCompanyFactImporter>();
            var scorer = scope.ServiceProvider.GetRequiredService<IRiskScoringService>();
            var alerts = scope.ServiceProvider.GetRequiredService<IAlertGenerationService>();
            progress.Report(10, $"Importing SEC data for {normalizedTicker}.");
            var result = await importer.ImportAsync(normalizedTicker, cancellationToken);
            progress.Report(80, "Recalculating expansion score.");
            await scorer.RecalculateAsync(cancellationToken);
            progress.Report(92, "Generating alerts.");
            await alerts.GenerateAsync(cancellationToken);
            return result;
        });

        return Accepted(job);
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

    [HttpPost("jobs/sec/all")]
    public IActionResult StartSecAllImportJob()
    {
        var job = importJobs.Start("SEC EDGAR all tracked", async (progress, cancellationToken) =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AiCapexDbContext>();
            var importer = scope.ServiceProvider.GetRequiredService<ISecCompanyFactImporter>();
            var scorer = scope.ServiceProvider.GetRequiredService<IRiskScoringService>();
            var alerts = scope.ServiceProvider.GetRequiredService<IAlertGenerationService>();
            var tickers = await scopedDb.Companies.OrderBy(x => x.Ticker).Select(x => x.Ticker).ToListAsync(cancellationToken);
            var results = new List<BulkImportItemDto>();
            for (var i = 0; i < tickers.Count; i++)
            {
                var ticker = tickers[i];
                progress.Report(5 + (int)Math.Round((i / (decimal)Math.Max(tickers.Count, 1)) * 70), $"Importing SEC data for {ticker}.");
                try
                {
                    var result = await importer.ImportAsync(ticker, cancellationToken);
                    results.Add(new BulkImportItemDto(ticker, result.FactsImported > 0 || result.MetricsImported > 0, result.Message, result.FactsImported, result.MetricsImported));
                }
                catch (Exception ex)
                {
                    results.Add(new BulkImportItemDto(ticker, false, ex.Message, 0, 0));
                }
            }

            progress.Report(82, "Recalculating expansion score.");
            await scorer.RecalculateAsync(cancellationToken);
            progress.Report(94, "Generating alerts.");
            await alerts.GenerateAsync(cancellationToken);
            return BulkImportSummary.Create("SEC EDGAR", results);
        });

        return Accepted(job);
    }

    [HttpPost("rss")]
    public async Task<IActionResult> ImportRss(CancellationToken cancellationToken)
    {
        var result = await rssImporter.ImportAsync(cancellationToken: cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("jobs/transcripts")]
    public IActionResult StartTranscriptImportJob()
    {
        var job = importJobs.Start("Recent transcripts", async (progress, cancellationToken) =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AiCapexDbContext>();
            var importer = scope.ServiceProvider.GetRequiredService<ITranscriptImportService>();
            var scorer = scope.ServiceProvider.GetRequiredService<IRiskScoringService>();
            var alerts = scope.ServiceProvider.GetRequiredService<IAlertGenerationService>();
            var tickers = await scopedDb.Companies.OrderBy(x => x.Ticker).Select(x => x.Ticker).ToListAsync(cancellationToken);
            progress.Report(10, "Importing recent transcripts for tracked companies.");
            var result = await importer.ImportRecentQuartersAsync(
                tickers,
                4,
                (completedCompanies, totalCompanies, ticker) =>
                    progress.Report(
                        10 + (int)Math.Round((completedCompanies / (decimal)Math.Max(totalCompanies, 1)) * 70),
                        $"Importing recent transcripts for {ticker}."),
                cancellationToken);
            progress.Report(82, "Recalculating expansion score.");
            await scorer.RecalculateAsync(cancellationToken);
            progress.Report(94, "Generating alerts.");
            await alerts.GenerateAsync(cancellationToken);
            return result;
        });

        return Accepted(job);
    }

    [HttpPost("jobs/rss")]
    public IActionResult StartRssImportJob()
    {
        var job = importJobs.Start("RSS/news feeds", async (progress, cancellationToken) =>
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<IRssImportService>();
            var scorer = scope.ServiceProvider.GetRequiredService<IRiskScoringService>();
            var alerts = scope.ServiceProvider.GetRequiredService<IAlertGenerationService>();
            progress.Report(10, "Fetching and analyzing RSS/news feeds.");
            var result = await importer.ImportAsync(
                (completedFeeds, totalFeeds, feedName) =>
                    progress.Report(
                        10 + (int)Math.Round((completedFeeds / (decimal)Math.Max(totalFeeds, 1)) * 68),
                        $"Fetching and analyzing RSS/news feed: {feedName}."),
                (completedFeeds, totalFeeds, feedName, completedItems, totalItems) =>
                    progress.Report(
                        ComputeRssProgress(10, 68, completedFeeds, totalFeeds, completedItems, totalItems),
                        $"Analyzing RSS/news item {completedItems + 1} of {totalItems} from {feedName}."),
                cancellationToken);
            progress.Report(80, "Recalculating expansion score.");
            await scorer.RecalculateAsync(cancellationToken);
            progress.Report(92, "Generating alerts.");
            await alerts.GenerateAsync(cancellationToken);
            return result;
        });

        return Accepted(job);
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

        var rss = await rssImporter.ImportAsync(cancellationToken: cancellationToken);
        var transcripts = await transcriptImporter.ImportRecentQuartersAsync(tickers, 4, cancellationToken: cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);
        return Ok(new
        {
            sec = BulkImportSummary.Create("SEC EDGAR", secResults),
            transcripts,
            rss
        });
    }

    [HttpPost("jobs/all")]
    public IActionResult StartAllImportsJob()
    {
        var job = importJobs.Start("All imports", async (progress, cancellationToken) =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AiCapexDbContext>();
            var scopedSec = scope.ServiceProvider.GetRequiredService<ISecCompanyFactImporter>();
            var scopedRss = scope.ServiceProvider.GetRequiredService<IRssImportService>();
            var scopedTranscripts = scope.ServiceProvider.GetRequiredService<ITranscriptImportService>();
            var scopedScoring = scope.ServiceProvider.GetRequiredService<IRiskScoringService>();
            var scopedAlerts = scope.ServiceProvider.GetRequiredService<IAlertGenerationService>();
            var tickers = await scopedDb.Companies.OrderBy(x => x.Ticker).Select(x => x.Ticker).ToListAsync(cancellationToken);
            var secResults = new List<BulkImportItemDto>();
            for (var i = 0; i < tickers.Count; i++)
            {
                var ticker = tickers[i];
                progress.Report(5 + (int)Math.Round((i / (decimal)Math.Max(tickers.Count, 1)) * 35), $"Importing SEC data for {ticker}.");
                try
                {
                    var result = await scopedSec.ImportAsync(ticker, cancellationToken);
                    secResults.Add(new BulkImportItemDto(ticker, result.FactsImported > 0 || result.MetricsImported > 0, result.Message, result.FactsImported, result.MetricsImported));
                }
                catch (Exception ex)
                {
                    secResults.Add(new BulkImportItemDto(ticker, false, ex.Message, 0, 0));
                }
            }

            progress.Report(45, "Fetching and analyzing RSS/news feeds.");
            var rss = await scopedRss.ImportAsync(
                (completedFeeds, totalFeeds, feedName) =>
                    progress.Report(
                        45 + (int)Math.Round((completedFeeds / (decimal)Math.Max(totalFeeds, 1)) * 24),
                        $"Fetching and analyzing RSS/news feed: {feedName}."),
                (completedFeeds, totalFeeds, feedName, completedItems, totalItems) =>
                    progress.Report(
                        ComputeRssProgress(45, 24, completedFeeds, totalFeeds, completedItems, totalItems),
                        $"Analyzing RSS/news item {completedItems + 1} of {totalItems} from {feedName}."),
                cancellationToken);
            progress.Report(70, "Importing recent transcripts.");
            var transcripts = await scopedTranscripts.ImportRecentQuartersAsync(
                tickers,
                4,
                (completedCompanies, totalCompanies, ticker) =>
                    progress.Report(
                        70 + (int)Math.Round((completedCompanies / (decimal)Math.Max(totalCompanies, 1)) * 17),
                        $"Importing recent transcripts for {ticker}."),
                cancellationToken);
            progress.Report(88, "Recalculating expansion score.");
            await scopedScoring.RecalculateAsync(cancellationToken);
            progress.Report(95, "Generating alerts.");
            await scopedAlerts.GenerateAsync(cancellationToken);
            return new
            {
                sec = BulkImportSummary.Create("SEC EDGAR", secResults),
                transcripts,
                rss
            };
        });

        return Accepted(job);
    }

    [HttpGet("jobs/{id:guid}")]
    public IActionResult GetImportJob(Guid id)
    {
        var job = importJobs.Get(id);
        return job is null ? NotFound() : Ok(job);
    }

    private async Task<IReadOnlyList<string>> GetTrackedTickers(CancellationToken cancellationToken) =>
        await db.Companies.OrderBy(x => x.Ticker).Select(x => x.Ticker).ToListAsync(cancellationToken);

    private static int ComputeRssProgress(int start, int width, int completedFeeds, int totalFeeds, int completedItems, int totalItems)
    {
        var feedFraction = completedItems / (decimal)Math.Max(totalItems, 1);
        var totalFraction = (completedFeeds + feedFraction) / Math.Max(totalFeeds, 1);
        return start + (int)Math.Round(totalFraction * width);
    }
}
