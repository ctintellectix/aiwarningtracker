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
        var service = new RiskScoringService(db);

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
        var service = new RiskScoringService(db);

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
        var service = new RiskScoringService(db);

        var result = await service.RecalculateAsync(new DateOnly(2026, 6, 15));

        var snapshot = await db.RiskScoreSnapshots.Include(x => x.FiscalQuarter).SingleAsync();
        Assert.Equal(currentCalendarQuarter.Id, snapshot.FiscalQuarterId);
        Assert.True(snapshot.HyperscalerCapexScore > 50);
        Assert.True(result.CurrentRiskScore > 50);
    }

    [Fact]
    public async Task Recalculate_backfills_recent_history_so_initial_delta_uses_previous_real_quarter()
    {
        await using var db = await CreateDbAsync();
        var q1 = await AddQuarterAsync(db, 2026, 1);
        var q2 = await AddQuarterAsync(db, 2026, 2);
        var q3 = await AddQuarterAsync(db, 2026, 3);
        var company = new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler", IsHyperscaler = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.FinancialMetrics.AddRange(
            CapexGrowth(company, q1, 10),
            CapexGrowth(company, q2, 30),
            CapexGrowth(company, q3, -40));
        await db.SaveChangesAsync();
        var service = new RiskScoringService(db);

        await service.RecalculateAsync(new DateOnly(2026, 9, 15));

        var snapshots = await db.RiskScoreSnapshots
            .Include(x => x.FiscalQuarter)
            .OrderBy(x => x.FiscalQuarter!.Year)
            .ThenBy(x => x.FiscalQuarter!.Quarter)
            .ToListAsync();
        Assert.Equal([q1.Id, q2.Id, q3.Id], snapshots.Select(x => x.FiscalQuarterId).ToArray());
        Assert.Equal(snapshots[2].Score - snapshots[1].Score, snapshots[2].ChangeFromPreviousQuarter);
        Assert.NotEqual(snapshots[2].Score - 50, snapshots[2].ChangeFromPreviousQuarter);
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
