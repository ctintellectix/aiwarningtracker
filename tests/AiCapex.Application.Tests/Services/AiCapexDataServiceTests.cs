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
    public async Task GetCompanyAsync_includes_company_derived_metric_signals_but_hides_keyword_transcript_signals()
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
        Assert.Contains(detail.Signals, x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow && x.ScoreImpact == -3.9m);
        Assert.Contains(detail.Signals, x => x.Category == RiskScoreCategory.HyperscalerCapexRevisionTrend && x.ScoreImpact == 5.8m);
        Assert.DoesNotContain(detail.Signals, x => x.Name.EndsWith("transcript signal", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(0, detail.Company.LatestMomentumSignal);
    }

    [Fact]
    public async Task GetCompaniesAsync_uses_unified_signal_scale()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.AiRevenueMonetization,
            SignalName = "Transcript AI narrative signal",
            ScoreImpact = 3,
            Confidence = 90
        });
        await db.SaveChangesAsync();

        var companies = await new AiCapexDataService(db).GetCompaniesAsync();

        Assert.Equal(3, companies.Single().LatestMomentumSignal);
    }

    [Fact]
    public async Task GetCompanyAsync_displays_unified_ai_signal_direction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.FinancialStressFreeCashFlow,
            SignalName = "Transcript AI narrative signal",
            Direction = SignalDirection.Neutral,
            ScoreImpact = 0.73m,
            Confidence = 90
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("MU");

        Assert.NotNull(detail);
        Assert.Equal(SignalDirection.Neutral, detail.CurrentSignals.Single().Direction);
    }

    [Fact]
    public async Task GetCompanyAsync_returns_display_scale_signal_values()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.FinancialStressFreeCashFlow,
            SignalName = "Transcript AI narrative signal",
            Direction = SignalDirection.Neutral,
            ScoreImpact = 0.73m,
            Confidence = 90
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("MU");

        Assert.NotNull(detail);
        Assert.Equal(0.7m, detail.CurrentSignals.Single().ScoreImpact);
    }

    [Fact]
    public async Task GetCompaniesAsync_matches_company_detail_latest_signal_when_derived_signals_exist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.AiRevenueMonetization,
            SignalName = "Transcript AI narrative signal",
            ScoreImpact = 3,
            Confidence = 90
        });
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            PeriodEndDate = quarter.PeriodEnd,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = 20,
            Unit = "%"
        });
        await db.SaveChangesAsync();

        var service = new AiCapexDataService(db);
        var listSignal = (await service.GetCompaniesAsync()).Single().LatestMomentumSignal;
        var detailSignal = (await service.GetCompanyAsync("AMD"))!.Company.LatestMomentumSignal;

        Assert.Equal(detailSignal, listSignal);
    }

    [Fact]
    public async Task GetCompanyAsync_scores_latest_signal_per_category_instead_of_historical_rows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var oldQuarter = new FiscalQuarter { Year = 2025, Quarter = 4, PeriodEnd = new DateOnly(2025, 12, 31) };
        var newQuarter = new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.AddRange(oldQuarter, newQuarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = oldQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = -8,
                Confidence = 90
            },
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = newQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 4,
                Confidence = 90
            });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("AMD");

        Assert.NotNull(detail);
        Assert.Equal(4, detail.Company.LatestMomentumSignal);
    }

    [Fact]
    public async Task GetCompanyAsync_ignores_zero_transcript_fallback_when_category_has_directional_signal()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.AiRevenueMonetization,
            SignalName = "Transcript AI narrative signal",
            ScoreImpact = 4,
            Confidence = 90
        });
        var transcript = new Transcript
        {
            CompanyId = company.Id,
            Ticker = "AMD",
            FiscalQuarterId = quarter.Id,
            Title = "AMD call",
            PublishedDate = quarter.PeriodEnd,
            PeriodEndDate = quarter.PeriodEnd,
            Text = "AI infrastructure"
        };
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();
        db.TranscriptMentions.Add(new TranscriptMention
        {
            TranscriptId = transcript.Id,
            KeywordGroup = "AI infrastructure",
            Keyword = "AI infrastructure",
            Count = 1,
            SentimentScore = 0,
            ContextSnippet = "AI infrastructure commentary."
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("AMD");

        Assert.NotNull(detail);
        Assert.Equal(4, detail.Company.LatestMomentumSignal);
    }

    [Fact]
    public async Task GetCompanyAsync_separates_current_signals_from_historical_signals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var oldQuarter = new FiscalQuarter { Year = 2025, Quarter = 4, PeriodEnd = new DateOnly(2025, 12, 31) };
        var newQuarter = new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.AddRange(oldQuarter, newQuarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = oldQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 2,
                Confidence = 90
            },
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = newQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 4,
                Confidence = 90
            });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("AMD");

        Assert.NotNull(detail);
        Assert.Single(detail.CurrentSignals);
        Assert.Equal("Q1 2026", detail.CurrentSignals.Single().Quarter);
        Assert.Single(detail.HistoricalSignals);
        Assert.Equal("Q4 2025", detail.HistoricalSignals.Single().Quarter);
    }

    [Fact]
    public async Task GetCompanyAsync_hides_keyword_transcript_evidence_when_ai_category_signals_exist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.AiRevenueMonetization,
            SignalName = "Transcript AI narrative signal",
            Name = "AMD current signal",
            ScoreImpact = 4,
            Confidence = 90
        });
        var transcript = new Transcript
        {
            CompanyId = company.Id,
            Ticker = "AMD",
            FiscalQuarterId = quarter.Id,
            Title = "AMD call",
            PublishedDate = quarter.PeriodEnd,
            PeriodEndDate = quarter.PeriodEnd,
            Text = "AI infrastructure"
        };
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();
        db.TranscriptMentions.Add(new TranscriptMention
        {
            TranscriptId = transcript.Id,
            KeywordGroup = "AI infrastructure",
            Keyword = "AI infrastructure",
            Count = 2,
            SentimentScore = 2,
            ContextSnippet = "AI infrastructure demand remains strong."
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("AMD");

        Assert.NotNull(detail);
        Assert.Single(detail.CurrentSignals);
        Assert.Equal("AMD current signal", detail.CurrentSignals.Single().Name);
        Assert.DoesNotContain(detail.Signals, signal => signal.Name.EndsWith("transcript signal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(detail.HistoricalSignals, signal => signal.Name.EndsWith("transcript signal", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task GetCompanyAsync_softens_capex_ocf_penalty_for_semiconductors_with_strong_current_demand()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM", IsSemiconductor = true };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.HbmDramPricingAllocation,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 0.92m,
                Confidence = 90
            },
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 0.84m,
                Confidence = 90
            },
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.FinancialStressFreeCashFlow,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 0.72m,
                Confidence = 90
            });
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            PeriodEndDate = quarter.PeriodEnd,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = 101,
            Unit = "%"
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("MU");

        Assert.NotNull(detail);
        var capexSignal = detail.CurrentSignals.Single(x => x.Name == "Capex / OCF derived signal");
        Assert.True(capexSignal.ScoreImpact > -80);
        Assert.True(detail.Company.LatestMomentumSignal > -10);
    }

    [Theory]
    [InlineData(15.7, 5.2)]
    [InlineData(48.7, 0.2)]
    [InlineData(50.8, -0.1)]
    [InlineData(81.7, -4.8)]
    [InlineData(101.0, -6.9)]
    public async Task GetCompanyAsync_uses_soft_capex_ocf_display_curve(decimal ratio, decimal expectedDisplayScore)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "TEST", Name = "Test", Segment = "General" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        db.FinancialMetrics.Add(new FinancialMetric
        {
            Company = company,
            FiscalQuarter = quarter,
            PeriodEndDate = quarter.PeriodEnd,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = ratio,
            Unit = "%"
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("TEST");

        Assert.NotNull(detail);
        Assert.Equal(expectedDisplayScore, detail.CurrentSignals.Single(x => x.Name == "Capex / OCF derived signal").ScoreImpact);
    }

    [Theory]
    [InlineData(-5.9, -1.0)]
    [InlineData(25.0, 3.9)]
    [InlineData(53.8, 7.1)]
    public async Task GetCompanyAsync_uses_soft_capex_growth_display_curve(decimal growth, decimal expectedDisplayScore)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "TEST", Name = "Test", Segment = "Hyperscaler", IsHyperscaler = true };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        db.FinancialMetrics.Add(new FinancialMetric
        {
            Company = company,
            FiscalQuarter = quarter,
            PeriodEndDate = quarter.PeriodEnd,
            Kind = MetricKind.CapexYoyGrowth,
            MetricName = "Capex YoY Growth",
            Value = growth,
            Unit = "%"
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("TEST");

        Assert.NotNull(detail);
        Assert.Equal(expectedDisplayScore, detail.CurrentSignals.Single(x => x.Name == "Capex growth derived signal").ScoreImpact);
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
        Assert.Equal("Neutral", summary.RiskBand);
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
    public async Task GetRiskScoreHistoryAsync_hides_first_change_when_no_prior_snapshot_exists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var q1 = new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) };
        var q2 = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        db.FiscalQuarters.AddRange(q1, q2);
        await db.SaveChangesAsync();
        db.RiskScoreSnapshots.AddRange(
            new RiskScoreSnapshot { FiscalQuarterId = q1.Id, Score = 54, ChangeFromPreviousQuarter = 4, Band = "Neutral", CreatedAt = DateTimeOffset.UtcNow },
            new RiskScoreSnapshot { FiscalQuarterId = q2.Id, Score = 57, ChangeFromPreviousQuarter = 3, Band = "Neutral", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var history = await new AiCapexDataService(db).GetRiskScoreHistoryAsync();

        Assert.Null(history[0].Change);
        Assert.Equal(3, history[1].Change);
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
    public async Task GetIndicatorTrends_prefers_summary_matching_aggregate_direction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var weakCompany = new Company { Ticker = "WEAK", Name = "Weak Co", Segment = "General" };
        var strongCompany = new Company { Ticker = "STRG", Name = "Strong Co", Segment = "General" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.AddRange(weakCompany, strongCompany);
        await db.SaveChangesAsync();
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                CompanyId = weakCompany.Id,
                FiscalQuarter = quarter,
                Category = RiskScoreCategory.FinancialStressFreeCashFlow,
                ScoreImpact = -7,
                Confidence = 1,
                Summary = "Capex intensity is elevated."
            },
            new IndicatorSignal
            {
                CompanyId = strongCompany.Id,
                FiscalQuarter = quarter,
                Category = RiskScoreCategory.FinancialStressFreeCashFlow,
                ScoreImpact = 6,
                Confidence = 95,
                Summary = "Cash generation remains strong."
            });
        await db.SaveChangesAsync();

        var trend = (await new AiCapexDataService(db).GetIndicatorTrendsAsync())
            .Single(x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow);

        Assert.Equal("Bullish", trend.Status);
        Assert.Equal("Cash generation remains strong.", trend.Summary);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_uses_category_rollups_for_top_positive_and_negative_indicators()
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

        Assert.Contains(summary.TopPositiveIndicators, x =>
            x.Category == RiskScoreCategory.HbmDramPricingAllocation &&
            x.Name == "HBM/DRAM derived signal");
        Assert.Contains(summary.TopNegativeIndicators, x =>
            x.Category == RiskScoreCategory.FinancialStressFreeCashFlow &&
            x.Name == "Financial stress/FCF derived signal");
        Assert.DoesNotContain(summary.TopPositiveIndicators, x => x.Name == "HBM sold-out allocation");
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
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.DataCenterPower && x.AverageSignal == 10);
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

        Assert.Contains(trends, x => x.Category == RiskScoreCategory.HbmDramPricingAllocation && x.AverageSignal == 0 && x.Status == "No data yet");
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.CowosAdvancedPackaging && x.AverageSignal == 0 && x.Status == "No data yet");
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.HyperscalerCapexRevisionTrend && x.AverageSignal == 3.9m && x.Status == "Bullish");
        Assert.Contains(trends, x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow && x.AverageSignal == -3.2m && x.Status == "Bearish");
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

        Assert.DoesNotContain(summary.TopPositiveIndicators, x => x.Category == RiskScoreCategory.HbmDramPricingAllocation);
        Assert.Contains(summary.TopNegativeIndicators, x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow && x.ScoreImpact == -4.6m);
        Assert.DoesNotContain("HBM remains allocated", summary.BullishSummary);
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

        Assert.DoesNotContain(summary.TopNegativeIndicators, x => x.Category == RiskScoreCategory.DataCenterPower);
        Assert.DoesNotContain("Power capacity is improving", summary.BearishSummary);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_leaves_negative_indicators_empty_when_all_current_signals_are_positive()
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
                Category = RiskScoreCategory.AiRevenueMonetization,
                ScoreImpact = 4,
                Confidence = 90,
                Summary = "AI revenue growth is strong."
            },
            new IndicatorSignal
            {
                FiscalQuarter = quarter,
                Category = RiskScoreCategory.CowosAdvancedPackaging,
                ScoreImpact = 2,
                Confidence = 90,
                Summary = "Packaging demand is tight."
            });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.Empty(summary.TopNegativeIndicators);
        Assert.Equal("No bearish real-data signals imported yet.", summary.BearishSummary);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_keeps_small_category_rollups_neutral()
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
            ScoreImpact = 0.5m,
            Confidence = 90,
            Summary = "Small mixed signal."
        });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        var signals = summary.TopPositiveIndicators
            .Concat(summary.TopNegativeIndicators)
            .Where(x => x.Category == RiskScoreCategory.DataCenterPower && x.Name.EndsWith("derived signal"))
            .ToList();
        Assert.NotEmpty(signals);
        Assert.All(signals, signal => Assert.Equal(SignalDirection.Neutral, signal.Direction));
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_does_not_mix_raw_company_signals_into_top_indicators()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var oldQuarter = new FiscalQuarter { Year = 2025, Quarter = 3, PeriodEnd = new DateOnly(2025, 9, 30) };
        var newQuarter = new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) };
        var company = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        db.FiscalQuarters.AddRange(oldQuarter, newQuarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = oldQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 2m,
                Confidence = 90,
                Summary = "Old stronger signal."
            },
            new IndicatorSignal
            {
                CompanyId = company.Id,
                FiscalQuarterId = newQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                ScoreImpact = 0.8m,
                Confidence = 90,
                Summary = "Current milder signal."
            });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.DoesNotContain(summary.TopPositiveIndicators, x => x.Ticker == "AMD");
        Assert.Contains(summary.TopPositiveIndicators, x =>
            x.Ticker is null &&
            x.Category == RiskScoreCategory.AiRevenueMonetization &&
            x.ScoreImpact == 0.8m);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_returns_top_company_drivers_from_current_company_signals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var oldQuarter = new FiscalQuarter { Year = 2025, Quarter = 4, PeriodEnd = new DateOnly(2025, 12, 31) };
        var currentQuarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var amd = new Company { Ticker = "AMD", Name = "AMD", Segment = "Semiconductor" };
        var mu = new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM" };
        db.FiscalQuarters.AddRange(oldQuarter, currentQuarter);
        db.Companies.AddRange(amd, mu);
        await db.SaveChangesAsync();
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                CompanyId = amd.Id,
                FiscalQuarterId = oldQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                Name = "AMD old signal",
                ScoreImpact = 9,
                Confidence = 90,
                Summary = "Old AMD signal."
            },
            new IndicatorSignal
            {
                CompanyId = amd.Id,
                FiscalQuarterId = currentQuarter.Id,
                Category = RiskScoreCategory.AiRevenueMonetization,
                SignalName = "Transcript AI narrative signal",
                Name = "AMD current signal",
                ScoreImpact = 6,
                Confidence = 90,
                Summary = "Current AMD signal."
            },
            new IndicatorSignal
            {
                CompanyId = mu.Id,
                FiscalQuarterId = currentQuarter.Id,
                Category = RiskScoreCategory.HbmDramPricingAllocation,
                SignalName = "Transcript AI narrative signal",
                Name = "MU current signal",
                ScoreImpact = -7,
                Confidence = 90,
                Summary = "Current MU signal."
            });
        await db.SaveChangesAsync();

        var summary = await new AiCapexDataService(db).GetDashboardSummaryAsync();

        Assert.Equal(2, summary.TopCompanyDrivers.Count);
        Assert.Equal("MU", summary.TopCompanyDrivers[0].Ticker);
        Assert.Equal(-7, summary.TopCompanyDrivers[0].ScoreImpact);
        Assert.Equal("AMD", summary.TopCompanyDrivers[1].Ticker);
        Assert.Equal(6, summary.TopCompanyDrivers[1].ScoreImpact);
        Assert.DoesNotContain(summary.TopCompanyDrivers, x => x.Name == "AMD old signal");
    }

    [Fact]
    public async Task GetCompanyAsync_includes_source_labels_for_imported_and_derived_signals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var quarter = new FiscalQuarter { Year = 2026, Quarter = 2, PeriodEnd = new DateOnly(2026, 6, 30) };
        var company = new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM" };
        db.FiscalQuarters.Add(quarter);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var source = new SourceDocument
        {
            CompanyId = company.Id,
            SourceType = SourceType.Transcript,
            Provider = "EarningsCallBiz",
            Title = "Micron call",
            Summary = "Transcript summary",
            Url = "https://example.com",
            PublishedDate = quarter.PeriodEnd
        };
        db.SourceDocuments.Add(source);
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            PeriodEndDate = quarter.PeriodEnd,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = 20,
            Unit = "%"
        });
        await db.SaveChangesAsync();
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.HbmDramPricingAllocation,
            SignalName = "Transcript AI narrative signal",
            Name = "Micron call",
            ScoreImpact = 6,
            Confidence = 90,
            SourceDocumentId = source.Id,
            Summary = "HBM remains tight."
        });
        await db.SaveChangesAsync();

        var detail = await new AiCapexDataService(db).GetCompanyAsync("MU");

        Assert.NotNull(detail);
        Assert.Contains(detail.CurrentSignals, x => x.Name == "Micron call" && x.SourceLabel == "Transcript - EarningsCallBiz");
        Assert.Contains(detail.CurrentSignals, x => x.Name == "Capex / OCF derived signal" && x.SourceLabel == "Derived financial metric");
    }
}
