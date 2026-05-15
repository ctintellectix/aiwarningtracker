using System.Text.Json;
using AiCapex.Application.Scoring;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Scoring;

public sealed class RiskScoringService(AiCapexDbContext db, RiskScoreWeights weights) : IRiskScoringService
{
    public async Task<RiskScoreRunResultDto> RecalculateAsync(CancellationToken cancellationToken = default)
        => await RecalculateAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

    public async Task<RiskScoreRunResultDto> RecalculateAsync(DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        await SchemaUpgrade.EnsureCompatibleSchemaAsync(db, cancellationToken);
        var quarter = await GetSnapshotQuarterAsync(asOfDate, cancellationToken);
        await BackfillRecentHistoryAsync(quarter, cancellationToken);
        return await CalculateAndSaveSnapshotAsync(quarter, cancellationToken);
    }

    private async Task BackfillRecentHistoryAsync(FiscalQuarter currentQuarter, CancellationToken cancellationToken)
    {
        var recentQuarters = await db.FiscalQuarters
            .Where(x => x.PeriodEnd <= currentQuarter.PeriodEnd)
            .OrderByDescending(x => x.PeriodEnd)
            .Take(8)
            .OrderBy(x => x.PeriodEnd)
            .ToListAsync(cancellationToken);
        var existingSnapshotQuarterIds = await db.RiskScoreSnapshots
            .Where(x => recentQuarters.Select(q => q.Id).Contains(x.FiscalQuarterId))
            .Select(x => x.FiscalQuarterId)
            .ToListAsync(cancellationToken);
        var existingSnapshotQuarterIdSet = existingSnapshotQuarterIds.ToHashSet();

        foreach (var quarter in recentQuarters.Where(x => x.Id != currentQuarter.Id && !existingSnapshotQuarterIdSet.Contains(x.Id)))
        {
            await CalculateAndSaveSnapshotAsync(quarter, cancellationToken);
        }
    }

    private async Task<RiskScoreRunResultDto> CalculateAndSaveSnapshotAsync(FiscalQuarter quarter, CancellationToken cancellationToken)
    {
        var signals = await db.IndicatorSignals
            .Include(x => x.FiscalQuarter)
            .Where(x => x.FiscalQuarter != null &&
                x.FiscalQuarter.PeriodEnd <= quarter.PeriodEnd)
            .ToListAsync(cancellationToken);
        signals = signals
            .GroupBy(x => new { x.Category, x.CompanyId })
            .SelectMany(x => x.OrderByDescending(v => v.FiscalQuarter!.PeriodEnd).Take(1))
            .ToList();
        var metrics = LatestMetrics(await db.FinancialMetrics
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .ToListAsync(cancellationToken), quarter.PeriodEnd);
        var inputs = BuildInputs(signals, metrics);
        var result = new RiskScoreCalculator(weights).Calculate(inputs);
        var previousScore = await GetPreviousQuarterScoreAsync(quarter, cancellationToken);
        var snapshot = await db.RiskScoreSnapshots.SingleOrDefaultAsync(x => x.FiscalQuarterId == quarter.Id, cancellationToken);
        if (snapshot is null)
        {
            snapshot = new RiskScoreSnapshot { FiscalQuarterId = quarter.Id };
            db.RiskScoreSnapshots.Add(snapshot);
        }

        snapshot.SnapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
        snapshot.Score = result.Score;
        snapshot.OverallScore = result.Score;
        snapshot.HyperscalerCapexScore = CategoryRisk(result, RiskScoreCategory.HyperscalerCapexRevisionTrend);
        snapshot.HbmDramScore = CategoryRisk(result, RiskScoreCategory.HbmDramPricingAllocation);
        snapshot.CowosPackagingScore = CategoryRisk(result, RiskScoreCategory.CowosAdvancedPackaging);
        snapshot.DataCenterPowerScore = CategoryRisk(result, RiskScoreCategory.DataCenterPower);
        snapshot.AiRevenueScore = CategoryRisk(result, RiskScoreCategory.AiRevenueMonetization);
        snapshot.FinancialStressScore = CategoryRisk(result, RiskScoreCategory.FinancialStressFreeCashFlow);
        snapshot.ChangeFromPreviousQuarter = result.Score - previousScore;
        snapshot.Band = result.Band;
        snapshot.ExplanationJson = JsonSerializer.Serialize(new
        {
            inputs = inputs.Select(x => new { category = x.Category.ToString(), x.Signal }),
            contributions = result.Contributions.Select(x => new { category = x.Category.ToString(), x.Signal, x.WeightedPoints })
        });
        snapshot.CreatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return new RiskScoreRunResultDto(result.Score, snapshot.ChangeFromPreviousQuarter, result.Band, "Expansion score recalculated from current indicator signals and financial metrics.");
    }

    private async Task<FiscalQuarter> GetSnapshotQuarterAsync(DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var currentCalendarQuarterNumber = ((asOfDate.Month - 1) / 3) + 1;
        var currentCalendarQuarterEnd = new DateOnly(
            asOfDate.Year,
            currentCalendarQuarterNumber * 3,
            DateTime.DaysInMonth(asOfDate.Year, currentCalendarQuarterNumber * 3));

        var quarter = await db.FiscalQuarters
            .Where(x => x.PeriodEnd <= currentCalendarQuarterEnd)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Quarter)
            .FirstOrDefaultAsync(cancellationToken);
        if (quarter is not null)
        {
            return quarter;
        }

        quarter = new FiscalQuarter
        {
            Year = asOfDate.Year,
            Quarter = currentCalendarQuarterNumber,
            PeriodEnd = currentCalendarQuarterEnd
        };
        db.FiscalQuarters.Add(quarter);
        await db.SaveChangesAsync(cancellationToken);
        return quarter;
    }

    private static IReadOnlyList<FinancialMetric> LatestMetrics(IReadOnlyList<FinancialMetric> metrics, DateOnly asOfPeriodEnd)
    {
        return metrics
            .Where(x => MetricPeriodEnd(x) <= asOfPeriodEnd)
            .GroupBy(x => new { x.CompanyId, Metric = x.MetricName ?? x.Kind.ToString() })
            .Select(x => x.OrderByDescending(MetricPeriodEnd).First())
            .ToList();
    }

    private static DateOnly MetricPeriodEnd(FinancialMetric metric) => metric.PeriodEndDate ?? metric.FiscalQuarter?.PeriodEnd ?? DateOnly.MinValue;

    private async Task<int> GetPreviousQuarterScoreAsync(FiscalQuarter current, CancellationToken cancellationToken)
    {
        var previous = await db.RiskScoreSnapshots
            .Include(x => x.FiscalQuarter)
            .Where(x => x.FiscalQuarter != null &&
                (x.FiscalQuarter.Year < current.Year ||
                 (x.FiscalQuarter.Year == current.Year && x.FiscalQuarter.Quarter < current.Quarter)))
            .OrderByDescending(x => x.FiscalQuarter!.Year)
            .ThenByDescending(x => x.FiscalQuarter!.Quarter)
            .FirstOrDefaultAsync(cancellationToken);
        return previous?.Score ?? 50;
    }

    private static IReadOnlyList<RiskScoreInput> BuildInputs(IReadOnlyList<IndicatorSignal> signals, IReadOnlyList<FinancialMetric> metrics)
    {
        var inputs = signals
            .GroupBy(x => x.Category)
            .Select(x => new RiskScoreInput(
                x.Key,
                CategorySignalAggregator.Aggregate(x.Select(v =>
                    (SignalScoreInterpreter.ToScoringSignal(v.ScoreImpact, v.SignalName), v.Confidence)))))
            .ToList();

        AddOrBlend(inputs, RiskScoreCategory.FinancialStressFreeCashFlow, FinancialStressSignal(metrics));
        AddOrBlend(inputs, RiskScoreCategory.HyperscalerCapexRevisionTrend, HyperscalerCapexSignal(metrics));
        return inputs;
    }

    private static decimal FinancialStressSignal(IReadOnlyList<FinancialMetric> metrics)
    {
        var capexRatios = metrics
            .Where(x => x.Kind == MetricKind.CapexAsPercentOfOperatingCashFlow || x.MetricName == "Capex / OCF")
            .Select(x => x.Value)
            .ToList();
        if (capexRatios.Count == 0)
        {
            return 0;
        }

        var averageRatio = capexRatios.Average();
        return DerivedFinancialSignalScorer.ScoreCapexOcf(averageRatio);
    }

    private static decimal HyperscalerCapexSignal(IReadOnlyList<FinancialMetric> metrics)
    {
        var growth = metrics
            .Where(x => x.Company?.IsHyperscaler == true &&
                (x.Kind == MetricKind.CapexQoqGrowth || x.Kind == MetricKind.CapexYoyGrowth || x.MetricName is "Capex QoQ Growth" or "Capex YoY Growth"))
            .Select(x => x.Value)
            .ToList();
        if (growth.Count == 0)
        {
            return 0;
        }

        var averageGrowth = growth.Average();
        return DerivedFinancialSignalScorer.ScoreCapexGrowth(averageGrowth);
    }

    private static void AddOrBlend(List<RiskScoreInput> inputs, RiskScoreCategory category, decimal signal)
    {
        if (signal == 0)
        {
            return;
        }

        var existingIndex = inputs.FindIndex(x => x.Category == category);
        if (existingIndex < 0)
        {
            inputs.Add(new RiskScoreInput(category, signal));
            return;
        }

        var existing = inputs[existingIndex];
        inputs[existingIndex] = existing with { Signal = (existing.Signal + signal) / 2m };
    }

    private static int CategoryRisk(RiskScoreResult result, RiskScoreCategory category)
    {
        var signal = result.Contributions.Single(x => x.Category == category).Signal;
        return (int)Math.Round(Math.Clamp((10m + signal) * 5m, 0m, 100m), MidpointRounding.AwayFromZero);
    }
}
