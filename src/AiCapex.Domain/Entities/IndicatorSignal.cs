using AiCapex.Domain.Scoring;

namespace AiCapex.Domain.Entities;

public sealed class IndicatorSignal
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public RiskScoreCategory Category { get; set; }
    public string Name { get; set; } = "";
    public SignalDirection Direction { get; set; }
    public decimal ScoreImpact { get; set; }
    public string Summary { get; set; } = "";
}
