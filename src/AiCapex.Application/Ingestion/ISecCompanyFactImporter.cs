using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Ingestion;

public interface ISecCompanyFactImporter
{
    Task<SecImportResultDto> ImportAsync(string ticker, CancellationToken cancellationToken = default);
}
