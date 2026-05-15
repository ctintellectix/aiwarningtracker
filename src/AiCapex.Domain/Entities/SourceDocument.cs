namespace AiCapex.Domain.Entities;

public sealed class SourceDocument
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public SourceType SourceType { get; set; }
    public string? Provider { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateOnly PublishedDate { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? RetrievedAtUtc { get; set; }
    public string? RawText { get; set; }
    public string? Snippet { get; set; }
    public decimal CredibilityWeight { get; set; } = 1m;
    public string Status { get; set; } = "Imported";
    public string? AnalysisProvider { get; set; }
    public string? AnalysisModel { get; set; }
    public string? AnalysisJson { get; set; }
}
