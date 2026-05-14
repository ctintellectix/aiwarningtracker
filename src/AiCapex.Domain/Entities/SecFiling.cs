namespace AiCapex.Domain.Entities;

public sealed class SecFiling
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public string AccessionNumber { get; set; } = "";
    public string FilingType { get; set; } = "";
    public DateOnly? FilingDate { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public string SecUrl { get; set; } = "";
    public string? RawJsonPath { get; set; }
    public DateTimeOffset ParsedAtUtc { get; set; }
}
