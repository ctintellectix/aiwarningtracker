using AiCapex.Application.Dashboard;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Transcripts;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.News;

public sealed class RssImportService(AiCapexDbContext db, IRssFeedClient client, IReadOnlyList<RssFeedOptions> feeds) : IRssImportService
{
    public async Task<ImportResultDto> ImportAsync(CancellationToken cancellationToken = default)
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
        foreach (var feed in feeds)
        {
            var entries = await client.FetchAsync(feed, cancellationToken);
            itemsFetched += entries.Count;
            foreach (var entry in entries)
            {
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
                var analyzer = new KeywordTranscriptAnalyzer();
                var mentions = analyzer.Analyze(text);
                if (mentions.Count == 0)
                {
                    continue;
                }

                var score = analyzer.ScoreDirectionalSignal(text);
                var signal = new IndicatorSignal
                {
                    SignalDate = publishedDate,
                    FiscalQuarterId = await EnsureFiscalQuarterIdAsync(publishedDate, cancellationToken),
                    Category = PickCategory(mentions),
                    Name = entry.Title,
                    SignalName = "RSS keyword signal",
                    Direction = score > 5 ? SignalDirection.Bullish : score < -5 ? SignalDirection.Bearish : SignalDirection.Neutral,
                    ScoreImpact = score,
                    Strength = Math.Min(100, mentions.Sum(x => x.Count) * 15),
                    Confidence = Math.Clamp((int)(feed.CredibilityWeight * 100), 0, 100),
                    SourceDocumentId = document.Id,
                    Summary = summary,
                    Explanation = $"Keyword groups: {string.Join(", ", mentions.Select(x => x.Group).Distinct())}"
                };
                db.IndicatorSignals.Add(signal);
                signalsImported++;
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

    private static RiskScoreCategory PickCategory(IReadOnlyList<TranscriptMentionResult> mentions)
    {
        var groups = mentions.Select(x => x.Group).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (groups.Contains("Memory/HBM"))
        {
            return RiskScoreCategory.HbmDramPricingAllocation;
        }

        if (groups.Contains("Packaging"))
        {
            return RiskScoreCategory.CowosAdvancedPackaging;
        }

        if (groups.Contains("Power"))
        {
            return RiskScoreCategory.DataCenterPower;
        }

        if (groups.Contains("Capex"))
        {
            return RiskScoreCategory.HyperscalerCapexRevisionTrend;
        }

        return RiskScoreCategory.AiRevenueMonetization;
    }
}
