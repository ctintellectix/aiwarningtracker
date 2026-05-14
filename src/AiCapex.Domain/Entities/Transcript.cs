namespace AiCapex.Domain.Entities;

public sealed class Transcript
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Ticker { get; set; } = "";
    public string? Market { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public int? FiscalYear { get; set; }
    public int? FiscalQuarterNumber { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public string? SourcePeriodLabel { get; set; }
    public DateOnly PublishedDate { get; set; }
    public DateOnly? CallDate { get; set; }
    public string? Provider { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? RawText { get; set; }
    public string? SourceUrl { get; set; }
    public DateTimeOffset? ImportedAtUtc { get; set; }
    public int ConfidenceScore { get; set; }
    public ICollection<TranscriptMention> Mentions { get; set; } = new List<TranscriptMention>();
}
