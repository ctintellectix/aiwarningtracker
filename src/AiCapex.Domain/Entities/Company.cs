namespace AiCapex.Domain.Entities;

public sealed class Company
{
    public int Id { get; set; }
    public string Ticker { get; set; } = "";
    public string Name { get; set; } = "";
    public string Segment { get; set; } = "";
    public ICollection<FinancialMetric> FinancialMetrics { get; set; } = new List<FinancialMetric>();
    public ICollection<Transcript> Transcripts { get; set; } = new List<Transcript>();
}
