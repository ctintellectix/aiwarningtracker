using AiCapex.Domain.Scoring;

namespace AiCapex.Domain.Entities;

public sealed class IndicatorSignal
{
    public int Id { get; set; }
    public DateOnly? SignalDate { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public RiskScoreCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? SignalName { get; set; }
    public SignalDirection Direction { get; set; }
    public decimal ScoreImpact { get; set; }
    public int Strength { get; set; }
    public int Confidence { get; set; }
    public int? SourceDocumentId { get; set; }
    public SourceDocument? SourceDocument { get; set; }
    public string Summary { get; set; } = "";
    public string? Explanation { get; set; }
}
