namespace AiCapex.Application.Ingestion;

public interface ITranscriptProviderChain
{
    Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default);
}

public interface ITranscriptStorageService
{
    Task<AiCapex.Application.Dashboard.ImportResultDto> StoreAsync(TranscriptResult transcript, CancellationToken cancellationToken = default);
}
