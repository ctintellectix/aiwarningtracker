using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Services;

public class AiCapexDataServiceTests
{
    [Fact]
    public async Task GetCompanyAsync_returns_company_detail_without_projection_filter_translation_error()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await SeedData.EnsureSeededAsync(db);

        var company = await new AiCapexDataService(db).GetCompanyAsync("MU");

        Assert.NotNull(company);
        Assert.Equal("MU", company.Company.Ticker);
        Assert.Contains(company.Signals, signal => signal.Category.ToString() == "HbmDramPricingAllocation");
    }

    [Fact]
    public async Task GetCompanyAsync_includes_company_derived_signals_from_real_metrics_and_transcripts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var transcript = new Transcript
        {
            CompanyId = company.Id,
            Ticker = "MSFT",
            FiscalQuarterId = quarter.Id,
            Title = "Microsoft Q2 call",
            PublishedDate = quarter.PeriodEnd,
            PeriodEndDate = quarter.PeriodEnd,
            Text = "AI infrastructure and capex commentary"
        };
        db.Transcripts.Add(transcript);
        db.FinancialMetrics.AddRange(
            new FinancialMetric
            {
                CompanyId = company.Id,
                FiscalQuarterId = quarter.Id,
                PeriodEndDate = quarter.PeriodEnd,
                Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
                MetricName = "Capex / OCF",
                Value = 75,
                Unit = "%"
            },
            new FinancialMetric
            {
                CompanyId = company.Id,
                FiscalQuarterId = quarter.Id,
                PeriodEndDate = quarter.PeriodEnd,
                Kind = MetricKind.CapexYoyGrowth,
                MetricName = "Capex YoY Growth",
                Value = 40,
                Unit = "%"
            });
        await db.SaveChangesAsync();
        db.TranscriptMentions.Add(new TranscriptMention
        {
            TranscriptId = transcript.Id,
            KeywordGroup = "AI infrastructure",
            Keyword = "AI infrastructure",
            Count = 3,
            SentimentScore = 20,
            ContextSnippet = "AI infrastructure demand remains strong."
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("MSFT");

        Assert.NotNull(detail);
        Assert.Contains(detail.Signals, x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow && x.ScoreImpact == -50);
        Assert.Contains(detail.Signals, x => x.Category == RiskScoreCategory.HyperscalerCapexRevisionTrend && x.ScoreImpact == 40);
        Assert.Contains(detail.Signals, x => x.Category == RiskScoreCategory.AiRevenueMonetization && x.ScoreImpact == 20);
        Assert.NotEqual(0, detail.Company.LatestRiskSignal);
    }

    [Fact]
    public async Task GetAlertsAsync_orders_alerts_without_sqlite_datetimeoffset_translation_error()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.WatchlistAlerts.AddRange(
            new WatchlistAlert
            {
                Severity = AlertSeverity.Warning,
                Title = "Older alert",
                Message = "First",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            },
            new WatchlistAlert
            {
                Severity = AlertSeverity.Critical,
                Title = "Newer alert",
                Message = "Second",
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var alerts = await new AiCapexDataService(db).GetAlertsAsync();

        Assert.Equal(["Newer alert", "Older alert"], alerts.Select(x => x.Title).ToArray());
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_returns_empty_state_when_no_score_snapshots_exist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.Equal(50, summary.CurrentRiskScore);
        Assert.Equal(0, summary.ChangeVsPreviousQuarter);
        Assert.Equal("Watch zone", summary.RiskBand);
        Assert.Empty(summary.ScoreHistory);
        Assert.Equal(Enum.GetValues<RiskScoreCategory>().Length, summary.CategoryStatuses.Count);
    }

    [Fact]
    public async Task GetRiskScoreHistoryAsync_returns_latest_eight_quarters_in_chronological_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();

        for (var index = 0; index < 10; index++)
        {
            var quarterNumber = (index % 4) + 1;
            var year = 2024 + (index / 4);
            var quarter = new FiscalQuarter
            {
                Year = year,
                Quarter = quarterNumber,
                PeriodEnd = new DateOnly(year, quarterNumber * 3, DateTime.DaysInMonth(year, quarterNumber * 3))
            };
            db.FiscalQuarters.Add(quarter);
            await db.SaveChangesAsync();
            db.RiskScoreSnapshots.Add(new RiskScoreSnapshot
            {
                FiscalQuarterId = quarter.Id,
                Score = 40 + index,
                ChangeFromPreviousQuarter = index,
                Band = "Watch zone",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var history = await new AiCapexDataService(db).GetRiskScoreHistoryAsync();

        Assert.Equal(8, history.Count);
        Assert.Equal("Q3 2024", history.First().Quarter);
        Assert.Equal("Q2 2026", history.Last().Quarter);
    }

    [Fact]
    public async Task GetIndicatorTrends_returns_plain_text_summaries_for_html_rss_signals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        db.FiscalQuarters.Add(quarter);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.DataCenterPower,
            Direction = SignalDirection.Neutral,
            ScoreImpact = 12,
            Summary = "<p data-block-key=\"abc\">Grid constraint &amp; substation backlog <a href=\"https://example.com\">read more</a></p>"
        });
        await db.SaveChangesAsync();

        var trends = await new AiCapexDataService(db).GetIndicatorTrendsAsync();

        Assert.Equal(Enum.GetValues<RiskScoreCategory>().Length, trends.Count);
        var summary = trends.Single(x => x.Category == RiskScoreCategory.DataCenterPower).Summary;
        Assert.Equal("Grid constraint & substation backlog read more", summary);
        Assert.DoesNotContain("<p", summary);
        Assert.DoesNotContain("&amp;", summary);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_uses_score_impact_for_top_positive_and_negative_indicators()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        db.FiscalQuarters.Add(quarter);
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                FiscalQuarter = quarter,
                Category = RiskScoreCategory.HbmDramPricingAllocation,
                Direction = SignalDirection.Neutral,
                ScoreImpact = 35,
                Name = "HBM sold-out allocation",
                Summary = "Allocation remains tight."
            },
            new IndicatorSignal
            {
                FiscalQuarter = quarter,
                Category = RiskScoreCategory.FinancialStressFreeCashFlow,
                Direction = SignalDirection.Neutral,
                ScoreImpact = -42,
                Name = "FCF pressure",
                Summary = "Cash flow pressure increased."
            });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.Contains(summary.TopPositiveIndicators, x => x.Name == "HBM sold-out allocation");
        Assert.Contains(summary.TopNegativeIndicators, x => x.Name == "FCF pressure");
    }

    [Fact]
    public async Task GetIndicatorTrends_returns_all_score_categories_when_some_have_no_signals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        db.FiscalQuarters.Add(quarter);
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            FiscalQuarter = quarter,
            Category = RiskScoreCategory.DataCenterPower,
            Direction = SignalDirection.Neutral,
            ScoreImpact = 12,
            Summary = "Power commentary remains mixed."
        });
        await db.SaveChangesAsync();

        var trends = await new AiCapexDataService(db).GetIndicatorTrendsAsync();

        Assert.Equal(Enum.GetValues<RiskScoreCategory>().Length, trends.Count);
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.DataCenterPower && x.AverageSignal == 12);
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.HbmDramPricingAllocation && x.AverageSignal == 0 && x.Status == "No data yet");
    }

    [Fact]
    public async Task GetIndicatorTrends_includes_transcript_and_financial_metric_derived_signals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var hyperscaler = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        var semiconductor = new Company { Ticker = "NVDA", Name = "NVIDIA", Segment = "Semiconductor", IsSemiconductor = true };
        db.FiscalQuarters.Add(quarter);
        db.Companies.AddRange(hyperscaler, semiconductor);
        await db.SaveChangesAsync();

        var transcript = new Transcript
        {
            CompanyId = semiconductor.Id,
            Ticker = "NVDA",
            FiscalQuarterId = quarter.Id,
            Title = "NVIDIA Q2 call",
            PublishedDate = quarter.PeriodEnd,
            Text = "HBM and CoWoS commentary"
        };
        db.Transcripts.Add(transcript);
        db.FinancialMetrics.AddRange(
            new FinancialMetric
            {
                CompanyId = hyperscaler.Id,
                FiscalQuarterId = quarter.Id,
                Kind = MetricKind.CapexYoyGrowth,
                MetricName = "Capex YoY Growth",
                Value = 25
            },
            new FinancialMetric
            {
                CompanyId = hyperscaler.Id,
                FiscalQuarterId = quarter.Id,
                Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
                MetricName = "Capex / OCF",
                Value = 70
            });
        await db.SaveChangesAsync();
        db.TranscriptMentions.AddRange(
            new TranscriptMention
            {
                TranscriptId = transcript.Id,
                KeywordGroup = "Memory/HBM",
                Keyword = "HBM",
                Count = 8,
                SentimentScore = 30,
                ContextSnippet = "HBM remains allocated."
            },
            new TranscriptMention
            {
                TranscriptId = transcript.Id,
                KeywordGroup = "Packaging",
                Keyword = "CoWoS",
                Count = 3,
                SentimentScore = 30,
                ContextSnippet = "CoWoS capacity remains tight."
            });
        await db.SaveChangesAsync();

        var trends = await new AiCapexDataService(db).GetIndicatorTrendsAsync();

        Assert.Contains(trends, x => x.Category == RiskScoreCategory.HbmDramPricingAllocation && x.AverageSignal == 30 && x.Status == "Constructive");
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.CowosAdvancedPackaging && x.AverageSignal == 30 && x.Status == "Constructive");
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.HyperscalerCapexRevisionTrend && x.AverageSignal == 25 && x.Status == "Constructive");
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow && x.AverageSignal == -40 && x.Status == "Weakening");
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_includes_derived_signals_in_top_indicators_and_summaries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var hyperscaler = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        var semiconductor = new Company { Ticker = "NVDA", Name = "NVIDIA", Segment = "Semiconductor", IsSemiconductor = true };
        db.FiscalQuarters.Add(quarter);
        db.Companies.AddRange(hyperscaler, semiconductor);
        await db.SaveChangesAsync();

        var transcript = new Transcript
        {
            CompanyId = semiconductor.Id,
            Ticker = "NVDA",
            FiscalQuarterId = quarter.Id,
            Title = "NVIDIA Q2 call",
            PublishedDate = quarter.PeriodEnd,
            Text = "HBM commentary"
        };
        db.Transcripts.Add(transcript);
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = hyperscaler.Id,
            FiscalQuarterId = quarter.Id,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = 80
        });
        await db.SaveChangesAsync();
        db.TranscriptMentions.Add(new TranscriptMention
        {
            TranscriptId = transcript.Id,
            KeywordGroup = "Memory/HBM",
            Keyword = "HBM",
            Count = 8,
            SentimentScore = 30,
            ContextSnippet = "HBM remains allocated."
        });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.Contains(summary.TopPositiveIndicators, x => x.Category == RiskScoreCategory.HbmDramPricingAllocation && x.ScoreImpact == 30);
        Assert.Contains(summary.TopNegativeIndicators, x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow && x.ScoreImpact == -60);
        Assert.Contains("HBM remains allocated", summary.BullishSummary);
        Assert.Contains("Average capex/OCF is 80.0%", summary.BearishSummary);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_uses_weakest_signals_for_bottom_indicators_when_no_bearish_signals_exist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "NVDA", Name = "NVIDIA", Segment = "Semiconductor" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var transcript = new Transcript
        {
            CompanyId = company.Id,
            Ticker = "NVDA",
            FiscalQuarterId = quarter.Id,
            Title = "NVIDIA Q2 call",
            PublishedDate = quarter.PeriodEnd,
            Text = "HBM and power commentary"
        };
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();
        db.TranscriptMentions.AddRange(
            new TranscriptMention
            {
                TranscriptId = transcript.Id,
                KeywordGroup = "Memory/HBM",
                Keyword = "HBM",
                Count = 8,
                SentimentScore = 40,
                ContextSnippet = "HBM remains allocated."
            },
            new TranscriptMention
            {
                TranscriptId = transcript.Id,
                KeywordGroup = "Power",
                Keyword = "power",
                Count = 3,
                SentimentScore = 10,
                ContextSnippet = "Power capacity is improving but still a constraint."
            });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.Contains(summary.TopNegativeIndicators, x => x.Category == RiskScoreCategory.DataCenterPower && x.ScoreImpact == 10);
        Assert.Contains("No bearish signals imported", summary.BearishSummary);
        Assert.Contains("Power capacity is improving", summary.BearishSummary);
    }
}
