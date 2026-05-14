using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Services;

public interface IDataSourceStatusService
{
    Task<IReadOnlyList<DataSourceStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);
}
