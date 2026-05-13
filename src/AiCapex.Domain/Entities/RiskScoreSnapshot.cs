namespace AiCapex.Domain.Entities;

public sealed class RiskScoreSnapshot
{
    public int Id { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public int Score { get; set; }
    public int ChangeFromPreviousQuarter { get; set; }
    public string Band { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
