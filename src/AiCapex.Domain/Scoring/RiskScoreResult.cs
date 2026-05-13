namespace AiCapex.Domain.Scoring;

public sealed record RiskScoreContribution(RiskScoreCategory Category, decimal Signal, decimal WeightedPoints);

public sealed record RiskScoreResult(int Score, string Band, IReadOnlyList<RiskScoreContribution> Contributions);
