using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Services;

public interface IAiCapexReadService
{
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MetricDto>> GetCompanyMetricsAsync(string ticker, CancellationToken cancellationToken = default);
    Task<CompanyFinancialsDto?> GetCompanyFinancialsAsync(string ticker, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryStatusDto>> GetIndicatorTrendsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranscriptSignalDto>> GetTranscriptSignalsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuarterScoreDto>> GetRiskScoreHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken cancellationToken = default);
}
