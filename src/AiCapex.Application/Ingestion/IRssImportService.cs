using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Ingestion;

public interface IRssImportService
{
    Task<ImportResultDto> ImportAsync(CancellationToken cancellationToken = default);
}
