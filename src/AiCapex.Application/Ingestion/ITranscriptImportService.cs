using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Ingestion;

public interface ITranscriptImportService
{
    Task<ImportResultDto> ImportAsync(string ticker, CancellationToken cancellationToken = default);
    Task<BulkImportResultDto> ImportRecentQuartersAsync(IReadOnlyList<string> tickers, int quarterCount = 4, CancellationToken cancellationToken = default);
}
