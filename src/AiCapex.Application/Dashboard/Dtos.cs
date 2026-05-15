using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;

namespace AiCapex.Application.Dashboard;

public sealed record DashboardSummaryDto(
    int CurrentRiskScore,
    int ChangeVsPreviousQuarter,
    string RiskBand,
    string BullishSummary,
    string BearishSummary,
    IReadOnlyList<SignalDto> TopPositiveIndicators,
    IReadOnlyList<SignalDto> TopNegativeIndicators,
    IReadOnlyList<SignalDto> TopCompanyDrivers,
    IReadOnlyList<CategoryStatusDto> CategoryStatuses,
    IReadOnlyList<QuarterScoreDto> ScoreHistory);

public sealed record CompanyDto(int Id, string Ticker, string Name, string Segment, double LatestMomentumSignal);

public sealed record CompanyDetailDto(
    CompanyDto Company,
    IReadOnlyList<MetricDto> Metrics,
    IReadOnlyList<SignalDto> Signals,
    IReadOnlyList<SignalDto> CurrentSignals,
    IReadOnlyList<SignalDto> HistoricalSignals,
    IReadOnlyList<SourceDocumentDto> Sources);

public sealed record MetricDto(string Quarter, string Kind, decimal Value, string Unit);

public sealed record SignalDto(string? Ticker, string Quarter, RiskScoreCategory Category, string Name, SignalDirection Direction, decimal ScoreImpact, string Summary);

public sealed record CategoryStatusDto(RiskScoreCategory Category, decimal AverageSignal, string Status, string Summary);

public sealed record QuarterScoreDto(string Quarter, int Score, int? Change, string Band);

public sealed record AlertDto(int Id, AlertSeverity Severity, string Title, string Message, DateTimeOffset CreatedAt, bool IsAcknowledged);

public sealed record SourceDocumentDto(string? Ticker, SourceType SourceType, string Title, string Url, string Summary, DateOnly PublishedDate);

public sealed record ManualEntryRequest(string Ticker, string Category, decimal ScoreImpact, string Summary, string SourceTitle);

public sealed record DataSourceStatusDto(string Source, bool IsConfigured, DateTimeOffset? LastSuccessfulImport, string Message);

public sealed record SecImportResultDto(string Ticker, bool UsedLiveData, int FactsImported, int MetricsImported, string Message);

public sealed record ImportResultDto(
    string Source,
    bool IsConfigured,
    int DocumentsImported,
    int SignalsImported,
    string Message,
    int ItemsFetched = 0,
    int DocumentsSkipped = 0);

public sealed record BulkImportItemDto(string Ticker, bool Success, string Message, int DocumentsImported, int SignalsImported);

public sealed record BulkImportResultDto(string Source, int CompaniesProcessed, int SuccessCount, int FailureCount, int DocumentsImported, int SignalsImported, IReadOnlyList<BulkImportItemDto> Results);

public sealed record CompanyFinancialsDto(
    CompanyDto Company,
    IReadOnlyList<MetricDto> Capex,
    IReadOnlyList<MetricDto> OperatingCashFlow,
    IReadOnlyList<MetricDto> CapexRatio,
    IReadOnlyList<MetricDto> Revenue,
    IReadOnlyList<MetricDto> Debt,
    IReadOnlyList<SourceDocumentDto> Sources);
