namespace AiCapex.Infrastructure.News;

public sealed class RssFeedOptions
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public decimal CredibilityWeight { get; set; } = 0.7m;
}
