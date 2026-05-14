using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Scoring;

public sealed record RiskScoreRunResultDto(int CurrentRiskScore, int ChangeVsPreviousQuarter, string RiskBand, string Message);

public interface IRiskScoringService
{
    Task<RiskScoreRunResultDto> RecalculateAsync(CancellationToken cancellationToken = default);
}
