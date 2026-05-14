namespace AiCapex.Infrastructure.News;

public sealed record RssFeedEntry(string Provider, string Title, string Url, string Summary, DateTimeOffset? PublishedAtUtc);
