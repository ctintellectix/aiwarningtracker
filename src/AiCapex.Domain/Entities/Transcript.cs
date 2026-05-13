namespace AiCapex.Domain.Entities;

public sealed class Transcript
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int FiscalQuarterId { get; set; }
    public FiscalQuarter? FiscalQuarter { get; set; }
    public DateOnly PublishedDate { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public ICollection<TranscriptMention> Mentions { get; set; } = new List<TranscriptMention>();
}
