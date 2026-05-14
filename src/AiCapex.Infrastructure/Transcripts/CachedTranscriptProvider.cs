using AiCapex.Application.Ingestion;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Transcripts;

public sealed class CachedTranscriptProvider(AiCapexDbContext db) : ITranscriptProvider
{
    public async Task<List<TranscriptMetadata>> GetTranscriptDatesAsync(string ticker, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        return await db.Transcripts
            .Where(x => x.Ticker == ticker || x.Company!.Ticker == ticker)
            .Select(x => new TranscriptMetadata(
                ticker,
                x.FiscalYear ?? x.FiscalQuarter!.Year,
                x.FiscalQuarterNumber ?? x.FiscalQuarter!.Quarter,
                x.CallDate,
                x.Provider ?? "Cached",
                x.Title,
                x.SourceUrl))
            .ToListAsync(cancellationToken);
    }

    public async Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        var transcripts = await db.Transcripts
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .ToListAsync(cancellationToken);
        var transcript = transcripts
            .Where(x => (string.Equals(x.Ticker, ticker, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Company?.Ticker, ticker, StringComparison.OrdinalIgnoreCase)) &&
                (x.FiscalYear ?? x.FiscalQuarter?.Year) == year &&
                (x.FiscalQuarterNumber ?? x.FiscalQuarter?.Quarter) == quarter)
            .OrderByDescending(x => x.ImportedAtUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return transcript is null
            ? null
            : new TranscriptResult(
                ticker,
                year,
                quarter,
                transcript.CallDate,
                transcript.Provider ?? "Cached",
                transcript.Title,
                transcript.RawText ?? transcript.Text,
                transcript.SourceUrl,
                transcript.ConfidenceScore == 0 ? 75 : transcript.ConfidenceScore,
                transcript.Market);
    }
}
