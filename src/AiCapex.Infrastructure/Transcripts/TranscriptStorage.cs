using AiCapex.Application.Dashboard;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Analysis;
using AiCapex.Application.Scoring;
using AiCapex.Application.Transcripts;
using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Transcripts;

public static class TranscriptStorage
{
    public static async Task<ImportResultDto> StoreAsync(
        AiCapexDbContext db,
        IDocumentNarrativeAnalysisService narrativeAnalysis,
        TranscriptResult transcript,
        CancellationToken cancellationToken)
    {
        var ticker = transcript.Ticker.ToUpperInvariant();
        await SchemaUpgrade.EnsureCompatibleSchemaAsync(db, cancellationToken);
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Ticker == ticker, cancellationToken);
        if (company is null)
        {
            return new ImportResultDto(transcript.Provider, true, 0, 0, "Company is not tracked.");
        }

        var existing = await db.Transcripts.AnyAsync(x =>
            x.CompanyId == company.Id &&
            x.Provider == transcript.Provider &&
            x.FiscalYear == transcript.FiscalYear &&
            x.FiscalQuarterNumber == transcript.FiscalQuarter,
            cancellationToken);

        if (existing)
        {
            return new ImportResultDto(transcript.Provider, true, 0, 0, "Transcript already imported.");
        }

        var quarter = await EnsureFiscalQuarterAsync(db, transcript.FiscalYear, transcript.FiscalQuarter, cancellationToken);
        var entity = new Transcript
        {
            CompanyId = company.Id,
            Ticker = ticker,
            Market = transcript.Market,
            FiscalQuarterId = quarter.Id,
            FiscalYear = transcript.FiscalYear,
            FiscalQuarterNumber = transcript.FiscalQuarter,
            PeriodEndDate = transcript.CallDate ?? quarter.PeriodEnd,
            SourcePeriodLabel = $"{ticker} FY{transcript.FiscalYear} Q{transcript.FiscalQuarter}",
            PublishedDate = transcript.CallDate ?? quarter.PeriodEnd,
            CallDate = transcript.CallDate,
            Provider = transcript.Provider,
            Title = transcript.Title,
            Text = transcript.RawText,
            RawText = transcript.RawText,
            SourceUrl = transcript.SourceUrl,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            ConfidenceScore = transcript.ConfidenceScore
        };
        db.Transcripts.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var analysis = await narrativeAnalysis.AnalyzeAsync(
            new DocumentNarrativeAnalysisRequest("Transcript", transcript.Title, transcript.RawText, ticker),
            cancellationToken);

        var sourceDocument = new SourceDocument
        {
            CompanyId = company.Id,
            SourceType = SourceType.Transcript,
            Provider = transcript.Provider,
            Title = transcript.Title,
            Url = transcript.SourceUrl ?? $"transcript://{ticker}/{transcript.FiscalYear}/Q{transcript.FiscalQuarter}",
            Summary = analysis.Summary,
            PublishedDate = transcript.CallDate ?? quarter.PeriodEnd,
            PublishedAtUtc = (transcript.CallDate ?? quarter.PeriodEnd).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            RawText = transcript.RawText.Length > 8000 ? transcript.RawText[..8000] : transcript.RawText,
            Snippet = BuildSnippet(transcript.RawText, ""),
            CredibilityWeight = 0.9m,
            Status = "Imported",
            AnalysisProvider = analysis.Provider,
            AnalysisModel = analysis.Model,
            AnalysisJson = analysis.RawJson
        };
        db.SourceDocuments.Add(sourceDocument);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var signal in analysis.Signals)
        {
            var interpretedScore = SignalScoreInterpreter.ToScoringSignal(
                signal.ScoreImpact,
                analysis.UsedFallback ? "Transcript keyword fallback signal" : "Transcript AI narrative signal");
            var displayScore = SignalScoreInterpreter.ToDisplaySignal(interpretedScore);
            db.IndicatorSignals.Add(new IndicatorSignal
            {
                CompanyId = company.Id,
                SignalDate = transcript.CallDate ?? quarter.PeriodEnd,
                FiscalQuarterId = quarter.Id,
                Category = signal.Category,
                Name = transcript.Title,
                SignalName = analysis.UsedFallback ? "Transcript keyword fallback signal" : "Transcript AI narrative signal",
                Direction = displayScore > 1 ? SignalDirection.Bullish : displayScore < -1 ? SignalDirection.Bearish : SignalDirection.Neutral,
                ScoreImpact = signal.ScoreImpact,
                Strength = Math.Min(100, Math.Abs((int)Math.Round(signal.ScoreImpact))),
                Confidence = signal.Confidence,
                SourceDocumentId = sourceDocument.Id,
                Summary = signal.Summary,
                Explanation = signal.Explanation
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ImportResultDto(transcript.Provider, true, 1, analysis.Signals.Count, "Transcript imported.");
    }

    private static async Task<FiscalQuarter> EnsureFiscalQuarterAsync(AiCapexDbContext db, int year, int quarterNumber, CancellationToken cancellationToken)
    {
        var quarter = await db.FiscalQuarters.SingleOrDefaultAsync(x => x.Year == year && x.Quarter == quarterNumber, cancellationToken);
        if (quarter is not null)
        {
            return quarter;
        }

        quarter = new FiscalQuarter { Year = year, Quarter = quarterNumber, PeriodEnd = new DateOnly(year, quarterNumber * 3, DateTime.DaysInMonth(year, quarterNumber * 3)) };
        db.FiscalQuarters.Add(quarter);
        await db.SaveChangesAsync(cancellationToken);
        return quarter;
    }

    private static string BuildSnippet(string text, string keywords)
    {
        var firstKeyword = keywords.Split(',').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && text.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (firstKeyword is null)
        {
            return text.Length > 240 ? text[..240] : text;
        }

        var index = text.IndexOf(firstKeyword, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, index - 80);
        var length = Math.Min(text.Length - start, firstKeyword.Length + 160);
        return text.Substring(start, length).Trim();
    }
}
