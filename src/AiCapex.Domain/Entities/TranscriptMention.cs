namespace AiCapex.Domain.Entities;

public sealed class TranscriptMention
{
    public int Id { get; set; }
    public int TranscriptId { get; set; }
    public Transcript? Transcript { get; set; }
    public string KeywordGroup { get; set; } = "";
    public string Keyword { get; set; } = "";
    public int Count { get; set; }
    public decimal SentimentScore { get; set; }
    public string? ContextSnippet { get; set; }
}
