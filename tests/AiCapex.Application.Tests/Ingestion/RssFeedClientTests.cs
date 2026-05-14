using AiCapex.Infrastructure.News;

namespace AiCapex.Application.Tests.Ingestion;

public class RssFeedClientTests
{
    [Fact]
    public void Parses_rss_items_into_source_entries()
    {
        const string xml = """
            <rss version="2.0">
              <channel>
                <title>Data Center Feed</title>
                <item>
                  <title>Power constraints delay data center capacity</title>
                  <link>https://example.com/power</link>
                  <description>Grid constraint and substation backlog commentary.</description>
                  <pubDate>Wed, 13 May 2026 12:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var entries = RssFeedParser.Parse(xml, "Data Center Feed").ToList();

        Assert.Single(entries);
        Assert.Equal("Power constraints delay data center capacity", entries[0].Title);
        Assert.Equal("https://example.com/power", entries[0].Url);
        Assert.Contains("Grid constraint", entries[0].Summary);
        Assert.Equal("Data Center Feed", entries[0].Provider);
    }

    [Fact]
    public void Parses_atom_entries_into_source_entries()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Semi Feed</title>
              <entry>
                <title>HBM allocation remains sold out</title>
                <link href="https://example.com/hbm" />
                <summary>Demand exceeds supply for HBM3E.</summary>
                <updated>2026-05-13T12:00:00Z</updated>
              </entry>
            </feed>
            """;

        var entries = RssFeedParser.Parse(xml, "Semi Feed").ToList();

        Assert.Single(entries);
        Assert.Equal("HBM allocation remains sold out", entries[0].Title);
        Assert.Equal("https://example.com/hbm", entries[0].Url);
    }

    [Fact]
    public void Parses_rss_html_descriptions_as_plain_text()
    {
        const string xml = """
            <rss version="2.0">
              <channel>
                <item>
                  <title>Power constraints</title>
                  <link>https://example.com/power</link>
                  <description><![CDATA[<p data-block-key="a1">Grid constraint &amp; cooling constraint <a href="https://example.com">read more</a></p>]]></description>
                </item>
              </channel>
            </rss>
            """;

        var entry = Assert.Single(RssFeedParser.Parse(xml, "Data Center Feed").ToList());

        Assert.Equal("Grid constraint & cooling constraint read more", entry.Summary);
    }
}
