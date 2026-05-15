using AiCapex.Application.Dashboard;
using AiCapex.Application.Ingestion;

namespace AiCapex.Infrastructure.Transcripts;

public interface ITranscriptImportClock
{
    (int Year, int Quarter) CurrentQuarter();
}

public sealed class SystemTranscriptImportClock : ITranscriptImportClock
{
    public (int Year, int Quarter) CurrentQuarter()
    {
        var today = DateTime.UtcNow;
        var quarter = ((today.Month - 1) / 3) + 1;
        return (today.Year, quarter);
    }
}

public sealed class TranscriptImportService(
    ITranscriptProviderChain providerChain,
    ITranscriptStorageService storage,
    ITranscriptImportClock clock) : ITranscriptImportService
{
    public async Task<ImportResultDto> ImportAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var summary = await ImportRecentQuartersAsync([ticker], 4, cancellationToken: cancellationToken);
        return new ImportResultDto(summary.Source, true, summary.DocumentsImported, summary.SignalsImported, $"Imported {summary.DocumentsImported} transcript documents for {ticker.ToUpperInvariant()}.");
    }

    public async Task<BulkImportResultDto> ImportRecentQuartersAsync(
        IReadOnlyList<string> tickers,
        int quarterCount = 4,
        Action<int, int, string>? onCompanyStarted = null,
        CancellationToken cancellationToken = default)
    {
        var quarters = LastQuarters(quarterCount);
        var results = new List<BulkImportItemDto>();
        var normalizedTickers = tickers.Select(x => x.ToUpperInvariant()).ToList();

        for (var index = 0; index < normalizedTickers.Count; index++)
        {
            var ticker = normalizedTickers[index];
            onCompanyStarted?.Invoke(index, normalizedTickers.Count, ticker);
            var documents = 0;
            var signals = 0;
            var attempts = 0;

            foreach (var (year, quarter) in quarters)
            {
                attempts++;
                var transcript = await providerChain.GetTranscriptAsync(ticker, year, quarter, cancellationToken);
                if (transcript is null)
                {
                    continue;
                }

                var stored = await storage.StoreAsync(transcript, cancellationToken);
                documents += stored.DocumentsImported;
                signals += stored.SignalsImported;
            }

            var message = documents > 0
                ? $"Imported {documents} transcript documents across {attempts} recent quarters."
                : $"No provider transcript found across {attempts} recent quarters.";
            results.Add(new BulkImportItemDto(ticker, true, message, documents, signals));
        }

        return BulkImportSummary.Create("Transcripts", results);
    }

    private IReadOnlyList<(int Year, int Quarter)> LastQuarters(int quarterCount)
    {
        var (year, quarter) = clock.CurrentQuarter();
        var quarters = new List<(int Year, int Quarter)>();
        for (var i = 0; i < quarterCount; i++)
        {
            quarters.Add((year, quarter));
            quarter--;
            if (quarter == 0)
            {
                quarter = 4;
                year--;
            }
        }

        return quarters;
    }
}
