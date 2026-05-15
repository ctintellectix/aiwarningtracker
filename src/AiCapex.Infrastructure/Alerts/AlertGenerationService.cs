using AiCapex.Application.Alerts;
using AiCapex.Application.Dashboard;
using AiCapex.Application.Scoring;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Alerts;

public sealed class AlertGenerationService(AiCapexDbContext db, AlertThresholdOptions thresholds) : IAlertGenerationService
{
    public async Task<ImportResultDto> GenerateAsync(CancellationToken cancellationToken = default)
    {
        await SchemaUpgrade.EnsureCompatibleSchemaAsync(db, cancellationToken);
        var created = 0;
        created += await GenerateScoreDeteriorationAlert(cancellationToken);
        created += await GenerateCapexOcfAlerts(cancellationToken);
        created += await GenerateCategoryWeakeningAlerts(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportResultDto("Watchlist alerts", true, created, 0, created == 0 ? "No alert thresholds triggered." : $"Generated {created} watchlist alerts.");
    }

    private async Task<int> GenerateScoreDeteriorationAlert(CancellationToken cancellationToken)
    {
        var latest = await db.RiskScoreSnapshots
            .Include(x => x.FiscalQuarter)
            .OrderByDescending(x => x.FiscalQuarter!.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null || latest.ChangeFromPreviousQuarter > -thresholds.ScoreDeteriorationPoints)
        {
            return 0;
        }

        var title = $"Expansion score weakened {Math.Abs(latest.ChangeFromPreviousQuarter)} points";
        var message = $"Expansion score moved to {latest.Score} ({latest.Band}) for {latest.FiscalQuarter?.Label ?? "latest period"}.";
        return await AddAlert(title, message, AlertSeverity.Warning, cancellationToken);
    }

    private async Task<int> GenerateCapexOcfAlerts(CancellationToken cancellationToken)
    {
        var metrics = await db.FinancialMetrics
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Where(x => x.Kind == MetricKind.CapexAsPercentOfOperatingCashFlow || x.MetricName == "Capex / OCF")
            .ToListAsync(cancellationToken);
        var latestStressMetrics = metrics
            .GroupBy(x => x.CompanyId)
            .Select(x => x.OrderByDescending(MetricPeriodEnd).First())
            .Where(x => x.Value >= thresholds.CapexOcfStressPercent)
            .ToList();

        var created = 0;
        foreach (var metric in latestStressMetrics)
        {
            var ticker = metric.Company?.Ticker ?? "Tracked company";
            var title = $"{ticker} Capex/OCF stress";
            var period = metric.SourcePeriodLabel ?? metric.FiscalQuarter?.Label ?? "latest period";
            var message = $"{ticker} capex as % of operating cash flow is {metric.Value:0.0}% for {period}, above the {thresholds.CapexOcfStressPercent:0}% alert threshold.";
            created += await AddAlert(title, message, AlertSeverity.Warning, cancellationToken);
        }

        return created;
    }


    private async Task<int> GenerateCategoryWeakeningAlerts(CancellationToken cancellationToken)
    {
        var signals = await db.IndicatorSignals
            .Include(x => x.FiscalQuarter)
            .Where(x => x.Category == RiskScoreCategory.HbmDramPricingAllocation ||
                 x.Category == RiskScoreCategory.CowosAdvancedPackaging ||
                 x.Category == RiskScoreCategory.DataCenterPower)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var signal in signals
            .GroupBy(x => new { x.Category, x.CompanyId })
            .Select(x => x.OrderByDescending(v => v.FiscalQuarter!.PeriodEnd).First())
            .Select(x => new
            {
                Signal = x,
                DisplayScore = SignalScoreInterpreter.ToDisplaySignal(
                    SignalScoreInterpreter.ToScoringSignal(x.ScoreImpact, x.SignalName))
            })
            .Where(x => x.DisplayScore <= thresholds.CategoryWeakeningSignal))
        {
            var label = CategoryLabel(signal.Signal.Category);
            var title = $"{label} signal weakened";
            var message = $"{label} signal impact is {signal.DisplayScore:0.0}: {signal.Signal.Summary}";
            created += await AddAlert(title, message, AlertSeverity.Warning, cancellationToken);
        }

        return created;
    }

    private async Task<int> AddAlert(string title, string message, AlertSeverity severity, CancellationToken cancellationToken)
    {
        var exists = await db.WatchlistAlerts.AnyAsync(x => x.Title == title && x.Message == message, cancellationToken);
        if (exists)
        {
            return 0;
        }

        db.WatchlistAlerts.Add(new WatchlistAlert
        {
            Severity = severity,
            Title = title,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return 1;
    }

    private static string CategoryLabel(RiskScoreCategory category) => category switch
    {
        RiskScoreCategory.HbmDramPricingAllocation => "HBM/DRAM",
        RiskScoreCategory.CowosAdvancedPackaging => "CoWoS/packaging",
        RiskScoreCategory.DataCenterPower => "Data center/power",
        _ => category.ToString()
    };

    private static DateOnly MetricPeriodEnd(FinancialMetric metric) => metric.PeriodEndDate ?? metric.FiscalQuarter?.PeriodEnd ?? DateOnly.MinValue;

}
