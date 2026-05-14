using AiCapex.Infrastructure.News;
using AiCapex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Ingestion;

public class RssImportServiceTests
{
    [Fact]
    public async Task Imports_rss_source_document_and_keyword_signal_once()
    {
        await using var db = await CreateDbAsync();
        var client = new StaticRssFeedClient([
            new RssFeedEntry("Semi Feed", "HBM allocation sold out", "https://example.com/hbm", "Demand exceeds supply for HBM and CoWoS capacity.", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero))
        ]);
        var service = new RssImportService(db, client, new[] { new RssFeedOptions { Name = "Semi Feed", Url = "https://example.com/feed" } });

        var first = await service.ImportAsync();
        var second = await service.ImportAsync();

        Assert.Equal(1, first.DocumentsImported);
        Assert.Equal(0, second.DocumentsImported);
        Assert.Equal(1, second.ItemsFetched);
        Assert.Equal(1, second.DocumentsSkipped);
        Assert.Contains("already imported", second.Message);
        Assert.Single(await db.SourceDocuments.ToListAsync());
        Assert.Contains(await db.IndicatorSignals.ToListAsync(), x => x.SignalName == "RSS keyword signal" && x.Strength > 0);
    }

    [Fact]
    public async Task Imports_rss_html_summary_as_plain_text()
    {
        await using var db = await CreateDbAsync();
        var client = new StaticRssFeedClient([
            new RssFeedEntry("Data Center Feed", "Power constraint", "https://example.com/power", "<p>Grid constraint &amp; substation backlog</p>", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero))
        ]);
        var service = new RssImportService(db, client, new[] { new RssFeedOptions { Name = "Data Center Feed", Url = "https://example.com/feed" } });

        await service.ImportAsync();

        var document = await db.SourceDocuments.SingleAsync();
        var signal = await db.IndicatorSignals.SingleAsync();
        Assert.Equal("Grid constraint & substation backlog", document.Summary);
        Assert.Equal("Grid constraint & substation backlog", signal.Summary);
    }

    private static async Task<AiCapexDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class StaticRssFeedClient(IReadOnlyList<RssFeedEntry> entries) : IRssFeedClient
    {
        public Task<IReadOnlyList<RssFeedEntry>> FetchAsync(RssFeedOptions feed, CancellationToken cancellationToken = default) =>
            Task.FromResult(entries);
    }
}
