namespace AiCapex.Application.Ingestion;

public sealed record TranscriptMetadata(
    string Ticker,
    int FiscalYear,
    int FiscalQuarter,
    DateOnly? CallDate,
    string Provider,
    string Title,
    string? SourceUrl);

public sealed record TranscriptResult(
    string Ticker,
    int FiscalYear,
    int FiscalQuarter,
    DateOnly? CallDate,
    string Provider,
    string Title,
    string RawText,
    string? SourceUrl,
    int ConfidenceScore,
    string? Market = null);

public interface ITranscriptProvider
{
    Task<List<TranscriptMetadata>> GetTranscriptDatesAsync(string ticker, CancellationToken cancellationToken = default);

    Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default);
}
