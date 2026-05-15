using AiCapex.Application.Dashboard;
using AiCapex.Application.Analysis;
using AiCapex.Application.Ingestion;
using AiCapex.Infrastructure.Persistence;

namespace AiCapex.Infrastructure.Transcripts;

public sealed class TranscriptStorageService(AiCapexDbContext db, IDocumentNarrativeAnalysisService narrativeAnalysis) : ITranscriptStorageService
{
    public Task<ImportResultDto> StoreAsync(TranscriptResult transcript, CancellationToken cancellationToken = default) =>
        TranscriptStorage.StoreAsync(db, narrativeAnalysis, transcript, cancellationToken);
}
