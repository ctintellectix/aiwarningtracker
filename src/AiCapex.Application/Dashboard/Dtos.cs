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
    IReadOnlyList<CategoryStatusDto> CategoryStatuses,
    IReadOnlyList<QuarterScoreDto> ScoreHistory);

public sealed record CompanyDto(int Id, string Ticker, string Name, string Segment, double LatestRiskSignal);

public sealed record CompanyDetailDto(
    CompanyDto Company,
    IReadOnlyList<MetricDto> Metrics,
    IReadOnlyList<SignalDto> Signals,
    IReadOnlyList<SourceDocumentDto> Sources);

public sealed record MetricDto(string Quarter, string Kind, decimal Value, string Unit);

public sealed record SignalDto(string? Ticker, string Quarter, RiskScoreCategory Category, string Name, SignalDirection Direction, decimal ScoreImpact, string Summary);

public sealed record CategoryStatusDto(RiskScoreCategory Category, decimal AverageSignal, string Status, string Summary);

public sealed record TranscriptSignalDto(string Ticker, string Quarter, string Title, string KeywordGroup, int Count, DateOnly PublishedDate);

public sealed record QuarterScoreDto(string Quarter, int Score, int Change, string Band);

public sealed record AlertDto(int Id, AlertSeverity Severity, string Title, string Message, DateTimeOffset CreatedAt, bool IsAcknowledged);

public sealed record SourceDocumentDto(string? Ticker, SourceType SourceType, string Title, string Url, string Summary, DateOnly PublishedDate);

public sealed record ManualEntryRequest(string Ticker, string Category, decimal ScoreImpact, string Summary, string SourceTitle);
