using System.Globalization;
using System.Xml.Linq;
using AiCapex.Infrastructure.Text;

namespace AiCapex.Infrastructure.News;

public static class RssFeedParser
{
    public static IEnumerable<RssFeedEntry> Parse(string xml, string provider)
    {
        var document = XDocument.Parse(xml);
        var rssItems = document.Descendants("item").ToList();
        if (rssItems.Count > 0)
        {
            foreach (var item in rssItems)
            {
                var title = ReadElement(item, "title");
                var url = ReadElement(item, "link");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                yield return new RssFeedEntry(
                    provider,
                    title,
                    url,
                    TextSanitizer.ToPlainText(ReadElement(item, "description")),
                    ParseDate(ReadElement(item, "pubDate")));
            }

            yield break;
        }

        XNamespace atom = "http://www.w3.org/2005/Atom";
        foreach (var entry in document.Descendants(atom + "entry"))
        {
            var title = entry.Element(atom + "title")?.Value;
            var url = entry.Elements(atom + "link").FirstOrDefault(x => (string?)x.Attribute("href") is not null)?.Attribute("href")?.Value;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            yield return new RssFeedEntry(
                provider,
                title,
                url,
                TextSanitizer.ToPlainText(entry.Element(atom + "summary")?.Value ?? entry.Element(atom + "content")?.Value),
                ParseDate(entry.Element(atom + "updated")?.Value ?? entry.Element(atom + "published")?.Value));
        }
    }

    private static string? ReadElement(XElement element, string name) => element.Element(name)?.Value?.Trim();

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
