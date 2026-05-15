using AiCapex.Domain.Scoring;

namespace AiCapex.Application.Analysis;

public interface IDocumentNarrativeAnalysisService
{
    Task<DocumentNarrativeAnalysisResult> AnalyzeAsync(
        DocumentNarrativeAnalysisRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentNarrativeAnalysisRequest(
    string DocumentType,
    string Title,
    string Text,
    string? Ticker = null);

public sealed record DocumentNarrativeAnalysisResult(
    string Provider,
    string? Model,
    bool UsedFallback,
    string Summary,
    int Confidence,
    IReadOnlyList<DocumentCategorySignal> Signals,
    string? RawJson = null);

public sealed record DocumentCategorySignal(
    RiskScoreCategory Category,
    decimal ScoreImpact,
    string Summary,
    string Explanation,
    int Confidence);
