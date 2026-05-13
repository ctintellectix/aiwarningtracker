namespace AiCapex.Application.Ingestion;

public interface ISecXbrlFinancialDataIngestor
{
    Task<string> IngestAsync(CancellationToken cancellationToken = default);
}

public interface ITranscriptIngestor
{
    Task<string> IngestAsync(CancellationToken cancellationToken = default);
}

public interface IManualIndicatorIngestor
{
    Task<string> IngestAsync(CancellationToken cancellationToken = default);
}

public interface INewsSourceIngestor
{
    Task<string> IngestAsync(CancellationToken cancellationToken = default);
}
