using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Ingestion;

public interface IRssImportService
{
    Task<ImportResultDto> ImportAsync(
        Action<int, int, string>? onFeedStarted = null,
        Action<int, int, string, int, int>? onEntryStarted = null,
        CancellationToken cancellationToken = default);
}
