namespace AiCapex.Domain.Scoring;

public sealed record RiskScoreInput(RiskScoreCategory Category, decimal Signal);
