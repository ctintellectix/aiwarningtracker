using AiCapex.Application.Ingestion;

namespace AiCapex.Infrastructure.Ingestion;

public sealed class StubSecXbrlFinancialDataIngestor : ISecXbrlFinancialDataIngestor
{
    public Task<string> IngestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("Stub SEC EDGAR XBRL ingestion complete. TODO: connect companyfacts/submissions APIs.");
}

public sealed class StubTranscriptIngestor : ITranscriptIngestor
{
    public Task<string> IngestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("Stub transcript ingestion complete. TODO: connect transcript provider.");
}

public sealed class StubManualIndicatorIngestor : IManualIndicatorIngestor
{
    public Task<string> IngestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("Manual entries are accepted through the API.");
}

public sealed class StubNewsSourceIngestor : INewsSourceIngestor
{
    public Task<string> IngestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("Stub RSS/news ingestion complete. TODO: connect feeds and source reliability.");
}
