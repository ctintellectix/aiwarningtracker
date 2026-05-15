using AiCapex.Application.Analysis;
using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.News;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Domain.Scoring;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Ingestion;

public class RssImportServiceTests
{
    [Fact]
    public async Task Imports_rss_source_document_and_ai_signal_once()
    {
        await using var db = await CreateDbAsync();
        var client = new StaticRssFeedClient([
            new RssFeedEntry("Semi Feed", "HBM allocation sold out", "https://example.com/hbm", "Demand exceeds supply for HBM and CoWoS capacity.", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero))
        ]);
        var service = new RssImportService(db, client, new[] { new RssFeedOptions { Name = "Semi Feed", Url = "https://example.com/feed" } }, new FakeNarrativeAnalysisService());

        var first = await service.ImportAsync();
        var second = await service.ImportAsync();

        Assert.Equal(1, first.DocumentsImported);
        Assert.Equal(0, second.DocumentsImported);
        Assert.Equal(1, second.ItemsFetched);
        Assert.Equal(1, second.DocumentsSkipped);
        Assert.Contains("already imported", second.Message);
        Assert.Single(await db.SourceDocuments.ToListAsync());
        Assert.Contains(await db.IndicatorSignals.ToListAsync(), x => x.SignalName == "RSS AI narrative signal" && x.Strength > 0);
    }

    [Fact]
    public async Task Imports_rss_html_summary_as_plain_text()
    {
        await using var db = await CreateDbAsync();
        var client = new StaticRssFeedClient([
            new RssFeedEntry("Data Center Feed", "Power constraint", "https://example.com/power", "<p>Grid constraint &amp; substation backlog</p>", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero))
        ]);
        var service = new RssImportService(db, client, new[] { new RssFeedOptions { Name = "Data Center Feed", Url = "https://example.com/feed" } }, new FakeNarrativeAnalysisService());

        await service.ImportAsync();

        var document = await db.SourceDocuments.SingleAsync();
        var signal = await db.IndicatorSignals.SingleAsync();
        Assert.Equal("Narrative summary.", document.Summary);
        Assert.Equal("Clean AI summary.", signal.Summary);
    }

    [Fact]
    public async Task Keeps_soft_ai_signal_neutral_when_assigning_direction()
    {
        await using var db = await CreateDbAsync();
        var client = new StaticRssFeedClient([
            new RssFeedEntry("Semi Feed", "HBM allocation remains strong", "https://example.com/hbm-soft", "Demand exceeds supply for HBM.", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero))
        ]);
        var service = new RssImportService(
            db,
            client,
            new[] { new RssFeedOptions { Name = "Semi Feed", Url = "https://example.com/feed" } },
            new SoftPositiveNarrativeAnalysisService());

        await service.ImportAsync();

        var signal = await db.IndicatorSignals.SingleAsync();
        Assert.Equal(SignalDirection.Neutral, signal.Direction);
    }

    [Fact]
    public async Task Sends_every_new_rss_item_to_openai_analysis()
    {
        await using var db = await CreateDbAsync();
        var client = new StaticRssFeedClient([
            new RssFeedEntry("General Feed", "Office lease renewed", "https://example.com/office", "The company renewed a downtown office lease.", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero))
        ]);
        var analyzer = new CountingNarrativeAnalysisService();
        var service = new RssImportService(db, client, new[] { new RssFeedOptions { Name = "General Feed", Url = "https://example.com/feed" } }, analyzer);

        var result = await service.ImportAsync();

        Assert.Equal(1, result.DocumentsImported);
        Assert.Equal(0, result.SignalsImported);
        Assert.Equal(1, analyzer.CallCount);
        Assert.Equal("OpenAI", (await db.SourceDocuments.SingleAsync()).AnalysisProvider);
    }

    [Fact]
    public async Task Reports_progress_before_each_feed_is_imported()
    {
        await using var db = await CreateDbAsync();
        var service = new RssImportService(
            db,
            new StaticRssFeedClient([]),
            [
                new RssFeedOptions { Name = "Semi Feed", Url = "https://example.com/semi" },
                new RssFeedOptions { Name = "Data Center Feed", Url = "https://example.com/datacenter" }
            ],
            new FakeNarrativeAnalysisService());
        var progress = new List<(int CompletedFeeds, int TotalFeeds, string FeedName)>();

        await service.ImportAsync(
            onFeedStarted: (completedFeeds, totalFeeds, feedName) =>
                progress.Add((completedFeeds, totalFeeds, feedName)));

        Assert.Equal(
            [(0, 2, "Semi Feed"), (1, 2, "Data Center Feed")],
            progress);
    }

    [Fact]
    public async Task Reports_progress_before_each_feed_entry_is_processed()
    {
        await using var db = await CreateDbAsync();
        var service = new RssImportService(
            db,
            new StaticRssFeedClient([
                new RssFeedEntry("Semi Feed", "First", "https://example.com/first", "HBM allocation sold out.", DateTimeOffset.UtcNow),
                new RssFeedEntry("Semi Feed", "Second", "https://example.com/second", "CoWoS capacity constrained.", DateTimeOffset.UtcNow)
            ]),
            [new RssFeedOptions { Name = "Semi Feed", Url = "https://example.com/semi" }],
            new FakeNarrativeAnalysisService());
        var progress = new List<(int CompletedFeeds, int TotalFeeds, string FeedName, int CompletedItems, int TotalItems)>();

        await service.ImportAsync(
            onEntryStarted: (completedFeeds, totalFeeds, feedName, completedItems, totalItems) =>
                progress.Add((completedFeeds, totalFeeds, feedName, completedItems, totalItems)));

        Assert.Equal(
            [(0, 1, "Semi Feed", 0, 2), (0, 1, "Semi Feed", 1, 2)],
            progress);
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

    private sealed class FakeNarrativeAnalysisService : IDocumentNarrativeAnalysisService
    {
        public Task<DocumentNarrativeAnalysisResult> AnalyzeAsync(DocumentNarrativeAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentNarrativeAnalysisResult(
                "OpenAI",
                "test-model",
                false,
                "Narrative summary.",
                90,
                [new DocumentCategorySignal(RiskScoreCategory.HbmDramPricingAllocation, 35, "Clean AI summary.", "Demand remains tight.", 88)]));
    }

    private sealed class CountingNarrativeAnalysisService : IDocumentNarrativeAnalysisService
    {
        public int CallCount { get; private set; }

        public Task<DocumentNarrativeAnalysisResult> AnalyzeAsync(DocumentNarrativeAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new DocumentNarrativeAnalysisResult("OpenAI", "test-model", false, "Unused", 90, []));
        }
    }

    private sealed class SoftPositiveNarrativeAnalysisService : IDocumentNarrativeAnalysisService
    {
        public Task<DocumentNarrativeAnalysisResult> AnalyzeAsync(DocumentNarrativeAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentNarrativeAnalysisResult(
                "OpenAI",
                "test-model",
                false,
                "Narrative summary.",
                90,
                [new DocumentCategorySignal(RiskScoreCategory.FinancialStressFreeCashFlow, 0.73m, "Constructive but measured.", "Positive cash flow evidence.", 88)]));
    }
}
