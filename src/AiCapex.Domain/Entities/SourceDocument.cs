namespace AiCapex.Domain.Entities;

public sealed class SourceDocument
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public SourceType SourceType { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateOnly PublishedDate { get; set; }
}
