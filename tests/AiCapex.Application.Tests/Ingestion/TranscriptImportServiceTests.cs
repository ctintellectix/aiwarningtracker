using AiCapex.Application.Ingestion;
using AiCapex.Infrastructure.Transcripts;

namespace AiCapex.Application.Tests.Ingestion;

public class TranscriptImportServiceTests
{
    [Fact]
    public async Task Imports_last_four_quarters_for_each_ticker_and_stores_hits()
    {
        var provider = new RecordingProvider(new Dictionary<(string Ticker, int Year, int Quarter), TranscriptResult>
        {
            [("NVDA", 2026, 1)] = Result("NVDA", 2026, 1),
            [("MSFT", 2025, 4)] = Result("MSFT", 2025, 4)
        });
        var storage = new RecordingStorage();
        var service = new TranscriptImportService(provider, storage, new FixedQuarterClock(2026, 1));

        var result = await service.ImportRecentQuartersAsync(["NVDA", "MSFT"]);

        Assert.Equal(8, provider.Calls.Count);
        Assert.Contains(provider.Calls, x => x == ("NVDA", 2026, 1));
        Assert.Contains(provider.Calls, x => x == ("NVDA", 2025, 4));
        Assert.Contains(provider.Calls, x => x == ("NVDA", 2025, 3));
        Assert.Contains(provider.Calls, x => x == ("NVDA", 2025, 2));
        Assert.Equal(2, storage.Stored.Count);
        Assert.Equal(2, result.DocumentsImported);
        Assert.Equal(2, result.SuccessCount);
    }

    private static TranscriptResult Result(string ticker, int year, int quarter) =>
        new(ticker, year, quarter, null, "EarningsCallBiz", $"{ticker} Q{quarter} {year}", "Operator earnings conference call quarter question answer ".PadRight(2200, 'x'), $"https://example.com/{ticker}", 80);

    private sealed class RecordingProvider(Dictionary<(string Ticker, int Year, int Quarter), TranscriptResult> results) : ITranscriptProviderChain
    {
        public List<(string Ticker, int Year, int Quarter)> Calls { get; } = [];

        public Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default)
        {
            Calls.Add((ticker, year, quarter));
            results.TryGetValue((ticker, year, quarter), out var result);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingStorage : ITranscriptStorageService
    {
        public List<TranscriptResult> Stored { get; } = [];

        public Task<AiCapex.Application.Dashboard.ImportResultDto> StoreAsync(TranscriptResult transcript, CancellationToken cancellationToken = default)
        {
            Stored.Add(transcript);
            return Task.FromResult(new AiCapex.Application.Dashboard.ImportResultDto(transcript.Provider, true, 1, 1, "stored"));
        }
    }

    private sealed class FixedQuarterClock(int year, int quarter) : ITranscriptImportClock
    {
        public (int Year, int Quarter) CurrentQuarter() => (year, quarter);
    }
}
