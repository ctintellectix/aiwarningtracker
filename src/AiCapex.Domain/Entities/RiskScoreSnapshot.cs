namespace AiCapex.Domain.Entities;

public sealed class RiskScoreSnapshot
{
    public int Id { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public DateOnly? SnapshotDate { get; set; }
    public int Score { get; set; }
    public int? OverallScore { get; set; }
    public int? HyperscalerCapexScore { get; set; }
    public int? HbmDramScore { get; set; }
    public int? CowosPackagingScore { get; set; }
    public int? DataCenterPowerScore { get; set; }
    public int? AiRevenueScore { get; set; }
    public int? FinancialStressScore { get; set; }
    public int ChangeFromPreviousQuarter { get; set; }
    public string Band { get; set; } = "";
    public string? ExplanationJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
