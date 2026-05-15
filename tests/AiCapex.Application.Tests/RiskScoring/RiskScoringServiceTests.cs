using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Scoring;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.RiskScoring;

public class RiskScoringServiceTests
{
    [Fact]
    public async Task Recalculate_uses_configured_weights()
    {
        await using var db = await CreateDbAsync();
        var quarter = await AddQuarterAsync(db, 2026, 1);
        db.IndicatorSignals.AddRange(
            new IndicatorSignal
            {
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.HyperscalerCapexRevisionTrend,
                ScoreImpact = 10,
                Confidence = 100
            },
            new IndicatorSignal
            {
                FiscalQuarterId = quarter.Id,
                Category = RiskScoreCategory.HbmDramPricingAllocation,
                ScoreImpact = -10,
                Confidence = 100
            });
        await db.SaveChangesAsync();
        var weights = new RiskScoreWeights(100, 0, 0, 0, 0, 0);

        var result = await new RiskScoringService(db, weights).RecalculateAsync();

        Assert.Equal(100, result.CurrentRiskScore);
    }

    [Fact]
    public async Task Recalculate_updates_latest_quarter_snapshot_from_indicator_signals()
    {
        await using var db = await CreateDbAsync();
        var quarter = await AddQuarterAsync(db, 2026, 1);
        var previous = await AddQuarterAsync(db, 2025, 4);
        db.RiskScoreSnapshots.Add(new RiskScoreSnapshot { FiscalQuarterId = previous.Id, Score = 40, Band = "Healthy expansion", CreatedAt = DateTimeOffset.UtcNow.AddDays(-90) });
        db.IndicatorSignals.Add(new IndicatorSignal
        {
            FiscalQuarterId = quarter.Id,
            Category = RiskScoreCategory.HyperscalerCapexRevisionTrend,
            Direction = SignalDirection.Bearish,
            ScoreImpact = -80,
            Name = "Capex guide cut",
            Summary = "Hyperscaler guidance weakened."
        });
        db.RiskScoreSnapshots.Add(new RiskScoreSnapshot { FiscalQuarterId = quarter.Id, Score = 50, Band = "Watch zone", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = new RiskScoringService(db, RiskScoreWeights.Default);

        var result = await service.RecalculateAsync();

        var snapshot = await db.RiskScoreSnapshots.SingleAsync(x => x.FiscalQuarterId == quarter.Id);
        Assert.Equal(result.CurrentRiskScore, snapshot.Score);
        Assert.True(snapshot.Score < 50);
        Assert.Equal(snapshot.Score - 40, snapshot.ChangeFromPreviousQuarter);
        Assert.Equal(snapshot.Score, snapshot.OverallScore);
    }

    [Fact]
    public async Task Recalculate_uses_financial_metrics_when_capex_ratio_is_stressed()
    {
        await using var db = await CreateDbAsync();
        var quarter = await AddQuarterAsync(db, 2026, 1);
        var company = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Kind = MetricKind.CapexAsPercentOfOperatingCashFlow,
            MetricName = "Capex / OCF",
            Value = 85,
            Unit = "%"
        });
        await db.SaveChangesAsync();
        var service = new RiskScoringService(db, RiskScoreWeights.Default);

        var result = await service.RecalculateAsync();

        Assert.True(result.CurrentRiskScore < 50);
        var snapshot = await db.RiskScoreSnapshots.SingleAsync();
        Assert.True(snapshot.FinancialStressScore < 50);
    }

    [Fact]
    public async Task Recalculate_uses_latest_available_company_periods_without_future_fiscal_labels()
    {
        await using var db = await CreateDbAsync();
        var currentCalendarQuarter = await AddQuarterAsync(db, 2026, 2);
        var providerFutureQuarter = await AddQuarterAsync(db, 2026, 3);
        var company = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.FinancialMetrics.AddRange(
            new FinancialMetric
            {
                CompanyId = company.Id,
                FiscalQuarterId = currentCalendarQuarter.Id,
                FiscalYear = 2026,
                FiscalQuarterNumber = 2,
                PeriodEndDate = new DateOnly(2026, 6, 30),
                Kind = MetricKind.CapexYoyGrowth,
                MetricName = "Capex YoY Growth",
                Value = 35,
                Unit = "%"
            },
            new FinancialMetric
            {
                CompanyId = company.Id,
                FiscalQuarterId = providerFutureQuarter.Id,
                FiscalYear = 2026,
                FiscalQuarterNumber = 3,
                PeriodEndDate = new DateOnly(2026, 9, 30),
                Kind = MetricKind.CapexYoyGrowth,
                MetricName = "Capex YoY Growth",
                Value = -90,
                Unit = "%"
            });
        await db.SaveChangesAsync();
        var service = new RiskScoringService(db, RiskScoreWeights.Default);

        var result = await service.RecalculateAsync(new DateOnly(2026, 6, 15));

        var snapshot = await db.RiskScoreSnapshots.Include(x => x.FiscalQuarter).SingleAsync();
        Assert.Equal(currentCalendarQuarter.Id, snapshot.FiscalQuarterId);
        Assert.True(snapshot.HyperscalerCapexScore > 50);
        Assert.True(result.CurrentRiskScore > 50);
    }

    [Fact]
    public async Task Recalculate_backfills_last_eight_quarters_so_initial_history_and_delta_use_real_quarters()
    {
        await using var db = await CreateDbAsync();
        var quarters = new List<FiscalQuarter>();
        for (var year = 2024; year <= 2026; year++)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                if (year == 2026 && quarter > 3)
                {
                    break;
                }

                quarters.Add(await AddQuarterAsync(db, year, quarter));
            }
        }

        var company = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.FinancialMetrics.AddRange(quarters.Select((quarter, index) => CapexGrowth(company, quarter, index * 5)));
        await db.SaveChangesAsync();
        var service = new RiskScoringService(db, RiskScoreWeights.Default);

        await service.RecalculateAsync(new DateOnly(2026, 9, 15));

        var snapshots = await db.RiskScoreSnapshots
            .Include(x => x.FiscalQuarter)
            .OrderBy(x => x.FiscalQuarter!.Year)
            .ThenBy(x => x.FiscalQuarter!.Quarter)
            .ToListAsync();
        var expectedQuarters = quarters.TakeLast(8).Select(x => x.Id).ToArray();
        Assert.Equal(expectedQuarters, snapshots.Select(x => x.FiscalQuarterId).ToArray());
        Assert.Equal(snapshots[^1].Score - snapshots[^2].Score, snapshots[^1].ChangeFromPreviousQuarter);
        Assert.NotEqual(snapshots[^1].Score - 50, snapshots[^1].ChangeFromPreviousQuarter);
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

    private static FinancialMetric CapexGrowth(Company company, FiscalQuarter quarter, decimal value) => new()
    {
        CompanyId = company.Id,
        FiscalQuarterId = quarter.Id,
        FiscalYear = quarter.Year,
        FiscalQuarterNumber = quarter.Quarter,
        PeriodEndDate = quarter.PeriodEnd,
        Kind = MetricKind.CapexYoyGrowth,
        MetricName = "Capex YoY Growth",
        Value = value,
        Unit = "%"
    };
}
