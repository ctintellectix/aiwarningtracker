namespace AiCapex.Domain.Entities;

public sealed class Company
{
    public int Id { get; set; }
    public string Ticker { get; set; } = "";
    public string Name { get; set; } = "";
    public string Segment { get; set; } = "";
    public string? Cik { get; set; }
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public string? ExchangeMarket { get; set; }
    public bool IsHyperscaler { get; set; }
    public bool IsSemiconductor { get; set; }
    public bool IsDataCenterInfrastructure { get; set; }
    public ICollection<FinancialMetric> FinancialMetrics { get; set; } = new List<FinancialMetric>();
    public ICollection<Transcript> Transcripts { get; set; } = new List<Transcript>();
}
