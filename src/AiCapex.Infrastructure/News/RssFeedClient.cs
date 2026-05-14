namespace AiCapex.Infrastructure.News;

public sealed class RssFeedClient(HttpClient httpClient) : IRssFeedClient
{
    public async Task<IReadOnlyList<RssFeedEntry>> FetchAsync(RssFeedOptions feed, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feed.Url))
        {
            return [];
        }

        using var response = await httpClient.GetAsync(feed.Url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return RssFeedParser.Parse(xml, feed.Name).ToList();
    }
}
