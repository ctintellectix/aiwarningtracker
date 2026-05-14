namespace AiCapex.Domain.Entities;

public sealed class FinancialMetric
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public MetricKind Kind { get; set; }
    public int? FiscalYear { get; set; }
    public int? FiscalQuarterNumber { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public string? SourcePeriodLabel { get; set; }
    public string? MetricName { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = "USD billions";
    public string? Source { get; set; }
    public string? SourceUrl { get; set; }
}
