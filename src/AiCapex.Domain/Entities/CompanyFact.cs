namespace AiCapex.Domain.Entities;

public sealed class CompanyFact
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Taxonomy { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Unit { get; set; } = "";
    public int FiscalYear { get; set; }
    public string FiscalPeriod { get; set; } = "";
    public string Form { get; set; } = "";
    public DateOnly? FiledDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal Value { get; set; }
    public string SourceUrl { get; set; } = "";
}
