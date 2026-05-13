namespace AiCapex.Domain.Entities;

public sealed class FinancialMetric
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public MetricKind Kind { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = "USD billions";
}
