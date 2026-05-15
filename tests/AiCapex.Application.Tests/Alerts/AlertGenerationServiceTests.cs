using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Application.Alerts;
using AiCapex.Infrastructure.Alerts;
using AiCapex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Alerts;

public class AlertGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_uses_configured_thresholds()
    {
        await using var db = await CreateDbAsync();
        var previous = await AddQuarterAsync(db, 2026, 1);
        var current = await AddQuarterAsync(db, 2026, 2);
        db.RiskScoreSnapshots.AddRange(
            new RiskScoreSnapshot { FiscalQuarterId = previous.Id, Score = 60, Band = "Strong", CreatedAt = DateTimeOffset.UtcNow.AddDays(-90) },
            new RiskScoreSnapshot { FiscalQuarterId = current.Id, Score = 57, ChangeFromPreviousQuarter = -3, Band = "Neutral", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new AlertGenerationService(db, new AlertThresholdOptions
        {
            ScoreDeteriorationPoints = 2,
            CapexOcfStressPercent = 90,
            CategoryWeakeningSignal = -6
        }).GenerateAsync();

        Assert.Contains(db.WatchlistAlerts, x => x.Title.Contains("Expansion score weakened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_creates_alerts_for_score_deterioration_and_capex_ocf_stress()
    {
        await using var db = await CreateDbAsync();
        var previous = await AddQuarterAsync(db, 2026, 1);
        var current = await AddQuarterAsync(db, 2026, 2);
        var company = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.RiskScoreSnapshots.AddRange(
            new RiskScoreSnapshot { FiscalQuarterId = previous.Id, Score = 75, Band = "Bullish acceleration", CreatedAt = DateTimeOffset.UtcNow.AddDays(-90) },
            new RiskScoreSnapshot { FiscalQuarterId = current.Id, Score = 58, ChangeFromPreviousQuarter = -17, Band = "Healthy expansion", CreatedAt = DateTimeOffset.UtcNow });
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = company.Id,
            FiscalQuarterId = current.Id,
            PeriodEndDate = current.PeriodEnd,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = 82,
            Unit = "%"
        });
        await db.SaveChangesAsync();

        var result = await new AlertGenerationService(db, AlertThresholdOptions.Default).GenerateAsync();

        Assert.Equal(2, result.DocumentsImported);
        Assert.Contains(db.WatchlistAlerts, x => x.Title.Contains("Expansion score weakened", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(db.WatchlistAlerts, x => x.Title.Contains("Capex/OCF stress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_ignores_keyword_transcript_mentions()
    {
        await using var db = await CreateDbAsync();
        var previous = await AddQuarterAsync(db, 2026, 1);
        var current = await AddQuarterAsync(db, 2026, 2);
        var company = new Company { Ticker = "NVDA", Name = "NVIDIA", Segment = "Accelerators" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var previousTranscript = new Transcript { CompanyId = company.Id, Ticker = "NVDA", FiscalQuarterId = previous.Id, PublishedDate = previous.PeriodEnd, Title = "Previous call", Text = "" };
        var currentTranscript = new Transcript { CompanyId = company.Id, Ticker = "NVDA", FiscalQuarterId = current.Id, PublishedDate = current.PeriodEnd, Title = "Current call", Text = "" };
        db.Transcripts.AddRange(previousTranscript, currentTranscript);
        await db.SaveChangesAsync();
        db.TranscriptMentions.AddRange(
            new TranscriptMention { TranscriptId = previousTranscript.Id, KeywordGroup = "Slowdown warning", Keyword = "delay", Count = 2 },
            new TranscriptMention { TranscriptId = currentTranscript.Id, KeywordGroup = "Slowdown warning", Keyword = "delay", Count = 12 });
        await db.SaveChangesAsync();

        await new AlertGenerationService(db, AlertThresholdOptions.Default).GenerateAsync();

        Assert.DoesNotContain(db.WatchlistAlerts, x => x.Title.Contains("Negative transcript terms rose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_creates_category_weakening_alerts_without_duplicates()
    {
        await using var db = await CreateDbAsync();
        var quarter = await AddQuarterAsync(db, 2026, 2);
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.HbmDramPricingAllocation,
            Direction = SignalDirection.Bearish,
            ScoreImpact = -45,
            Name = "HBM pricing pressure",
            Summary = "Pricing pressure increased."
        });
        await db.SaveChangesAsync();
        var service = new AlertGenerationService(db, AlertThresholdOptions.Default);

        await service.GenerateAsync();
        await service.GenerateAsync();

        var alerts = await db.WatchlistAlerts.ToListAsync();
        Assert.Single(alerts, x => x.Title.Contains("HBM/DRAM signal weakened", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAsync_uses_display_scale_for_category_weakening_alerts()
    {
        await using var db = await CreateDbAsync();
        var quarter = await AddQuarterAsync(db, 2026, 2);
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.HbmDramPricingAllocation,
                SignalName = "Transcript AI narrative signal",
                Direction = SignalDirection.Bearish,
                ScoreImpact = -4m,
                Name = "Mild HBM concern",
                Summary = "Mild pressure."
            },
            new IndicatorSignal
            {
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.CowosAdvancedPackaging,
                SignalName = "Transcript AI narrative signal",
                Direction = SignalDirection.Bearish,
                ScoreImpact = -2m,
                Name = "Mild CoWoS concern",
                Summary = "Mild pressure."
            });
        await db.SaveChangesAsync();

        await new AlertGenerationService(db, AlertThresholdOptions.Default).GenerateAsync();

        var alerts = await db.WatchlistAlerts.ToListAsync();
        Assert.Contains(alerts, x =>
            x.Title.Contains("HBM/DRAM signal weakened", StringComparison.OrdinalIgnoreCase) &&
            x.Message.Contains("-4.0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(alerts, x => x.Title.Contains("CoWoS/packaging signal weakened", StringComparison.OrdinalIgnoreCase));
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

    private static async Task<FiscalQuarter> AddQuarterAsync(AiCapexDbContext db, int year, int quarterNumber)
    {
        var quarter = new FiscalQuarter
        {
            Year = year,
            Quarter = quarterNumber,
            PeriodEnd = new DateOnly(year, quarterNumber * 3, DateTime.DaysInMonth(year, quarterNumber * 3))
        };
        db.FiscalQuarters.Add(quarter);
        await db.SaveChangesAsync();
        return quarter;
    }
}
