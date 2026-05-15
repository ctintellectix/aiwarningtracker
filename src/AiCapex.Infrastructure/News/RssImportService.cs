using AiCapex.Application.Dashboard;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Analysis;
using AiCapex.Application.Scoring;
using AiCapex.Application.Transcripts;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.News;

public sealed class RssImportService(
    AiCapexDbContext db,
    IRssFeedClient client,
    IReadOnlyList<RssFeedOptions> feeds,
    IDocumentNarrativeAnalysisService narrativeAnalysis) : IRssImportService
{
    public async Task<ImportResultDto> ImportAsync(
        Action<int, int, string>? onFeedStarted = null,
        Action<int, int, string, int, int>? onEntryStarted = null,
        CancellationToken cancellationToken = default)
    {
        await SchemaUpgrade.EnsureCompatibleSchemaAsync(db, cancellationToken);
        if (feeds.Count == 0)
        {
            return new ImportResultDto("RSS/news feeds", true, 0, 0, "No RSS feeds configured.");
        }

        var documentsImported = 0;
        var signalsImported = 0;
        var itemsFetched = 0;
        var documentsSkipped = 0;
        for (var feedIndex = 0; feedIndex < feeds.Count; feedIndex++)
        {
            var feed = feeds[feedIndex];
            onFeedStarted?.Invoke(feedIndex, feeds.Count, feed.Name);
            var entries = await client.FetchAsync(feed, cancellationToken);
            itemsFetched += entries.Count;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                onEntryStarted?.Invoke(feedIndex, feeds.Count, feed.Name, entryIndex, entries.Count);
                var summary = TextSanitizer.ToPlainText(entry.Summary);
                var exists = await db.SourceDocuments.AnyAsync(x => x.Url == entry.Url, cancellationToken);
                if (exists)
                {
                    documentsSkipped++;
                    continue;
                }

                var publishedDate = DateOnly.FromDateTime((entry.PublishedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime);
                var document = new SourceDocument
                {
                    SourceType = SourceType.NewsRss,
                    Provider = entry.Provider,
                    Title = entry.Title,
                    Url = entry.Url,
                    Summary = summary,
                    PublishedDate = publishedDate,
                    PublishedAtUtc = entry.PublishedAtUtc,
                    RetrievedAtUtc = DateTimeOffset.UtcNow,
                    RawText = $"{entry.Title}\n{summary}",
                    CredibilityWeight = feed.CredibilityWeight
                };
                db.SourceDocuments.Add(document);
                await db.SaveChangesAsync(cancellationToken);
                documentsImported++;

                var text = $"{entry.Title}\n{summary}";
                var analysis = await narrativeAnalysis.AnalyzeAsync(
                    new DocumentNarrativeAnalysisRequest("RSS", entry.Title, text),
                    cancellationToken);
                document.Summary = analysis.Summary;
                document.AnalysisProvider = analysis.Provider;
                document.AnalysisModel = analysis.Model;
                document.AnalysisJson = analysis.RawJson;
                if (analysis.Signals.Count == 0)
                {
                    continue;
                }

                foreach (var analysisSignal in analysis.Signals)
                {
                    var score = analysisSignal.ScoreImpact;
                    var signalName = "RSS AI narrative signal";
                    var interpretedScore = SignalScoreInterpreter.ToScoringSignal(score, signalName);
                    var displayScore = SignalScoreInterpreter.ToDisplaySignal(interpretedScore);
                    db.IndicatorSignals.Add(new IndicatorSignal
                    {
                        SignalDate = publishedDate,
                        FiscalQuarterId = await EnsureFiscalQuarterIdAsync(publishedDate, cancellationToken),
                        Category = analysisSignal.Category,
                        Name = entry.Title,
                        SignalName = signalName,
                        Direction = displayScore > 1 ? SignalDirection.Bullish : displayScore < -1 ? SignalDirection.Bearish : SignalDirection.Neutral,
                        ScoreImpact = score,
                        Strength = Math.Min(100, Math.Abs((int)Math.Round(score))),
                        Confidence = Math.Min(analysisSignal.Confidence, Math.Clamp((int)(feed.CredibilityWeight * 100), 0, 100)),
                        SourceDocumentId = document.Id,
                        Summary = analysisSignal.Summary,
                        Explanation = analysisSignal.Explanation
                    });
                    signalsImported++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var message = documentsImported == 0 && documentsSkipped > 0
            ? $"Fetched {itemsFetched} RSS/news items; all {documentsSkipped} were already imported."
            : $"Fetched {itemsFetched} RSS/news items; imported {documentsImported} new documents and skipped {documentsSkipped} duplicates.";
        return new ImportResultDto("RSS/news feeds", true, documentsImported, signalsImported, message, itemsFetched, documentsSkipped);
    }

    private async Task<int> EnsureFiscalQuarterIdAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var quarterNumber = ((date.Month - 1) / 3) + 1;
        var quarter = await db.FiscalQuarters.SingleOrDefaultAsync(x => x.Year == date.Year && x.Quarter == quarterNumber, cancellationToken);
        if (quarter is null)
        {
            quarter = new FiscalQuarter { Year = date.Year, Quarter = quarterNumber, PeriodEnd = new DateOnly(date.Year, quarterNumber * 3, DateTime.DaysInMonth(date.Year, quarterNumber * 3)) };
            db.FiscalQuarters.Add(quarter);
            await db.SaveChangesAsync(cancellationToken);
        }

        return quarter.Id;
    }

}
