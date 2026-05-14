namespace AiCapex.Infrastructure.News;

public interface IRssFeedClient
{
    Task<IReadOnlyList<RssFeedEntry>> FetchAsync(RssFeedOptions feed, CancellationToken cancellationToken = default);
}
