using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Alerts;

public interface IAlertGenerationService
{
    Task<ImportResultDto> GenerateAsync(CancellationToken cancellationToken = default);
}
