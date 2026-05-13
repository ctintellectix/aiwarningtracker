using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Services;

public interface IManualEntryService
{
    Task<SignalDto> AddManualEntryAsync(ManualEntryRequest request, CancellationToken cancellationToken = default);
}
