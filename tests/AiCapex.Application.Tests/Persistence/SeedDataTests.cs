using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Persistence;

public class SeedDataTests
{
    [Fact]
    public async Task EnsureSeeded_includes_sndk_as_memory_hbm_company()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);

        await SeedData.EnsureSeededAsync(db);

        var company = await db.Companies.SingleAsync(x => x.Ticker == "SNDK");
        Assert.Equal("SanDisk", company.Name);
        Assert.Equal("Memory/HBM", company.Segment);
    }

    [Fact]
    public async Task EnsureSeeded_adds_missing_sndk_to_existing_seeded_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Companies.Add(new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler" });
        await db.SaveChangesAsync();

        await SeedData.EnsureSeededAsync(db);

        var company = await db.Companies.SingleAsync(x => x.Ticker == "SNDK");
        Assert.Equal("Memory/HBM", company.Segment);
    }

    [Fact]
    public async Task EnsureSeeded_adds_sample_transcript_mentions_for_multiple_tracked_companies()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);

        await SeedData.EnsureSeededAsync(db);

        var tickers = await db.TranscriptMentions
            .Include(x => x.Transcript)!.ThenInclude(x => x!.Company)
            .Select(x => x.Transcript!.Company!.Ticker)
            .Distinct()
            .ToListAsync();
        Assert.Contains("NVDA", tickers);
        Assert.Contains("MSFT", tickers);
        Assert.Contains("MU", tickers);
        Assert.Contains("VRT", tickers);
        Assert.True(tickers.Count >= 8);
    }

    [Fact]
    public async Task EnsureSeeded_backfills_sample_transcripts_for_existing_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Companies.Add(new Company { Ticker = "NVDA", Name = "NVIDIA", Segment = "Accelerators" });
        await db.SaveChangesAsync();

        await SeedData.EnsureSeededAsync(db);

        var transcriptTickers = await db.Transcripts
            .Include(x => x.Company)
            .Select(x => x.Company!.Ticker)
            .Distinct()
            .ToListAsync();
        Assert.Contains("MSFT", transcriptTickers);
        Assert.Contains("MU", transcriptTickers);
    }

    [Fact]
    public async Task EnsureSeeded_real_data_mode_keeps_tracked_companies_without_sample_observations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);

        await SeedData.EnsureSeededAsync(db, new SeedDataOptions { UseSampleData = false });

        Assert.Equal(17, await db.Companies.CountAsync());
        Assert.Empty(await db.FinancialMetrics.ToListAsync());
        Assert.Empty(await db.Transcripts.ToListAsync());
        Assert.Empty(await db.TranscriptMentions.ToListAsync());
        Assert.Empty(await db.IndicatorSignals.ToListAsync());
        Assert.Empty(await db.RiskScoreSnapshots.ToListAsync());
        Assert.Empty(await db.WatchlistAlerts.ToListAsync());
    }

    [Fact]
    public async Task EnsureSeeded_real_data_mode_purges_existing_sample_observations_but_keeps_real_imports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await SeedData.EnsureSeededAsync(db);
        var company = await db.Companies.SingleAsync(x => x.Ticker == "MSFT");
        var quarter = await db.FiscalQuarters.FirstAsync();
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Source = "SEC EDGAR",
            Kind = MetricKind.QuarterlyCapex,
            Value = 10
        });
        db.Transcripts.Add(new Transcript
        {
            CompanyId = company.Id,
            Ticker = company.Ticker,
            FiscalQuarterId = quarter.Id,
            Provider = "Finnhub",
            Title = "Real provider transcript",
            Text = "AI infrastructure demand exceeds supply.",
            RawText = "AI infrastructure demand exceeds supply.",
            PublishedDate = quarter.PeriodEnd
        });
        db.RiskScoreSnapshots.Add(new RiskScoreSnapshot
        {
            FiscalQuarterId = quarter.Id,
            Score = 45,
            Band = "Healthy expansion",
            ExplanationJson = "{\"from\":\"old demo-calculated data\"}"
        });
        await db.SaveChangesAsync();

        await SeedData.EnsureSeededAsync(db, new SeedDataOptions { UseSampleData = false });

        Assert.Equal(17, await db.Companies.CountAsync());
        Assert.Contains(await db.FinancialMetrics.ToListAsync(), x => x.Source == "SEC EDGAR");
        Assert.Contains(await db.Transcripts.ToListAsync(), x => x.Provider == "Finnhub");
        Assert.DoesNotContain(await db.Transcripts.ToListAsync(), x => x.Provider == "SampleSeed" || x.Title.Contains("sample", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.IndicatorSignals.Where(x => x.Name == "HBM allocation remains tight").ToListAsync());
        Assert.Empty(await db.RiskScoreSnapshots.ToListAsync());
    }
}
