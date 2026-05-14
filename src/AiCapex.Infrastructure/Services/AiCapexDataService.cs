using AiCapex.Application.Dashboard;
using AiCapex.Application.Services;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Services;

public sealed class AiCapexDataService(AiCapexDbContext db) : IAiCapexReadService, IManualEntryService
{
    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var history = await GetRiskScoreHistoryAsync(cancellationToken);
        var latest = history.LastOrDefault() ?? new QuarterScoreDto("No score yet", 50, 0, "Watch zone");
        var categories = await GetIndicatorTrendsAsync(cancellationToken);
        var signals = (await SignalQuery().ToListAsync(cancellationToken))
            .Concat(BuildCategoryDashboardSignals(categories, latest.Quarter))
            .ToList();
        var bullishSignals = signals.Where(x => x.ScoreImpact > 0).OrderByDescending(x => x.ScoreImpact).Take(4).ToList();
        var bearishSignals = signals.Where(x => x.ScoreImpact < 0).OrderBy(x => x.ScoreImpact).Take(4).ToList();
        var bearishSummary = BuildSignalSummary(bearishSignals, "No bearish real-data signals imported yet.");
        if (bearishSignals.Count == 0 && bullishSignals.Count > 0)
        {
            bearishSignals = signals.Where(x => x.ScoreImpact > 0).OrderBy(x => x.ScoreImpact).Take(4).ToList();
            bearishSummary = $"No bearish signals imported yet. Weakest current signals: {BuildSignalSummary(bearishSignals, "")}";
        }

        return new DashboardSummaryDto(
            latest.Score,
            latest.Change,
            latest.Band,
            BuildSignalSummary(bullishSignals, "No bullish real-data signals imported yet."),
            bearishSummary,
            bullishSignals,
            bearishSignals,
            categories,
            history);
    }

    public async Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Companies
            .OrderBy(x => x.Ticker)
            .Select(x => new CompanyDto(
                x.Id,
                x.Ticker,
                x.Name,
                x.Segment,
                db.IndicatorSignals.Where(s => s.CompanyId == x.Id).Select(s => (int?)s.ScoreImpact).Average() ?? 0))
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Ticker == ticker.ToUpper(), cancellationToken);
        if (company is null)
        {
            return null;
        }

        var metrics = await GetCompanyMetricsAsync(company.Ticker, cancellationToken);
        var signals = await db.IndicatorSignals
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Where(x => x.CompanyId == company.Id)
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                x.Direction,
                x.ScoreImpact,
                x.Summary))
            .ToListAsync(cancellationToken);
        signals.AddRange(await GetCompanyDerivedSignalsAsync(company, cancellationToken));
        var latestRiskSignal = signals.Count == 0 ? 0 : Math.Round(signals.Average(x => x.ScoreImpact), 1);
        var dto = new CompanyDto(company.Id, company.Ticker, company.Name, company.Segment, (double)latestRiskSignal);

        var sources = await db.SourceDocuments
            .Include(x => x.Company)
            .Where(x => x.CompanyId == company.Id)
            .Select(x => new SourceDocumentDto(
                x.Company == null ? null : x.Company.Ticker,
                x.SourceType,
                x.Title,
                x.Url,
                x.Summary,
                x.PublishedDate))
            .ToListAsync(cancellationToken);

        return new CompanyDetailDto(dto, metrics, signals, sources);
    }

    private async Task<IReadOnlyList<SignalDto>> GetCompanyDerivedSignalsAsync(Company company, CancellationToken cancellationToken)
    {
        var asOfPeriodEnd = CurrentCalendarQuarterEnd();
        var signals = new List<SignalDto>();
        var latestMetrics = (await db.FinancialMetrics
                .Include(x => x.FiscalQuarter)
                .Where(x => x.CompanyId == company.Id)
                .ToListAsync(cancellationToken))
            .Where(x => MetricPeriodEnd(x) <= asOfPeriodEnd)
            .GroupBy(x => x.MetricName ?? x.Kind.ToString())
            .Select(x => x.OrderByDescending(MetricPeriodEnd).First())
            .ToList();
        var latestMetricQuarter = latestMetrics
            .OrderByDescending(MetricPeriodEnd)
            .FirstOrDefault();

        var capexRatio = latestMetrics
            .FirstOrDefault(x => x.Kind == MetricKind.CapexAsPercentOfOperatingCashFlow || x.MetricName == "Capex / OCF");
        if (capexRatio is not null)
        {
            var impact = Math.Round(Math.Clamp((50m - capexRatio.Value) * 2m, -80m, 40m), 1);
            signals.Add(new SignalDto(
                company.Ticker,
                SignalQuarter(latestMetricQuarter?.FiscalQuarter, latestMetricQuarter?.SourcePeriodLabel),
                RiskScoreCategory.FinancialStressFreeCashFlow,
                "Capex / OCF derived signal",
                DirectionFromImpact(impact),
                impact,
                $"Latest capex/OCF is {capexRatio.Value:0.0}%."));
        }

        var capexGrowthValues = latestMetrics
            .Where(x => x.Kind == MetricKind.CapexQoqGrowth || x.Kind == MetricKind.CapexYoyGrowth || x.MetricName is "Capex QoQ Growth" or "Capex YoY Growth")
            .Select(x => x.Value)
            .ToList();
        if (company.IsHyperscaler && capexGrowthValues.Count > 0)
        {
            var averageGrowth = capexGrowthValues.Average();
            var impact = Math.Round(Math.Clamp(averageGrowth, -70m, 60m), 1);
            signals.Add(new SignalDto(
                company.Ticker,
                SignalQuarter(latestMetricQuarter?.FiscalQuarter, latestMetricQuarter?.SourcePeriodLabel),
                RiskScoreCategory.HyperscalerCapexRevisionTrend,
                "Capex growth derived signal",
                DirectionFromImpact(impact),
                impact,
                $"Latest capex growth average is {averageGrowth:0.0}%."));
        }

        var mentions = await db.TranscriptMentions
            .Include(x => x.Transcript)!.ThenInclude(x => x!.FiscalQuarter)
            .Where(x => x.Transcript != null && x.Transcript.CompanyId == company.Id)
            .ToListAsync(cancellationToken);
        var latestTranscriptId = mentions
            .Where(x => x.Transcript is not null && TranscriptPeriodEnd(x.Transcript) <= asOfPeriodEnd)
            .GroupBy(x => x.Transcript!.CompanyId)
            .Select(x => x
                .OrderByDescending(v => TranscriptPeriodEnd(v.Transcript!))
                .ThenByDescending(v => v.Transcript!.ImportedAtUtc)
                .First()
                .TranscriptId)
            .FirstOrDefault();

        signals.AddRange(mentions
            .Where(x => x.TranscriptId == latestTranscriptId)
            .GroupBy(x => MapTranscriptGroup(x.KeywordGroup))
            .Select(group =>
            {
                var strongestMention = group.OrderByDescending(x => x.Count).First();
                var impact = Math.Round(group.Average(x => x.SentimentScore), 1);
                return new SignalDto(
                    company.Ticker,
                    SignalQuarter(strongestMention.Transcript?.FiscalQuarter, strongestMention.Transcript?.SourcePeriodLabel),
                    group.Key,
                    $"{FormatCategoryName(group.Key)} transcript signal",
                    DirectionFromImpact(impact),
                    impact,
                    TextSanitizer.ToPlainText(strongestMention.ContextSnippet ?? $"{strongestMention.KeywordGroup} transcript mentions from {strongestMention.Transcript?.Title ?? "imported transcript"}."));
            }));

        return signals
            .OrderByDescending(x => Math.Abs(x.ScoreImpact))
            .ThenBy(x => x.Category.ToString())
            .ToList();
    }

    public async Task<IReadOnlyList<MetricDto>> GetCompanyMetricsAsync(string ticker, CancellationToken cancellationToken = default)
    {
        return await db.FinancialMetrics
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Where(x => x.Company != null && x.Company.Ticker == ticker.ToUpper())
            .OrderBy(x => x.FiscalQuarter!.Year)
            .ThenBy(x => x.FiscalQuarter!.Quarter)
            .Select(x => new MetricDto(x.FiscalQuarter!.Label, x.MetricName ?? x.Kind.ToString(), x.Value, x.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyFinancialsDto?> GetCompanyFinancialsAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(ticker, cancellationToken);
        if (company is null)
        {
            return null;
        }

        var metrics = await GetCompanyMetricsAsync(ticker, cancellationToken);
        return new CompanyFinancialsDto(
            company.Company,
            metrics.Where(x => x.Kind is "Quarterly Capex" or "QuarterlyCapex" or "TTM Capex" or "Capex QoQ Growth" or "Capex YoY Growth").ToList(),
            metrics.Where(x => x.Kind is "Operating Cash Flow" or "OperatingCashFlow").ToList(),
            metrics.Where(x => x.Kind is "Capex / OCF" or "CapexAsPercentOfOperatingCashFlow").ToList(),
            metrics.Where(x => x.Kind == "Revenue").ToList(),
            metrics.Where(x => x.Kind == "Debt").ToList(),
            company.Sources);
    }

    public async Task<IReadOnlyList<CategoryStatusDto>> GetIndicatorTrendsAsync(CancellationToken cancellationToken = default)
    {
        var asOfPeriodEnd = CurrentCalendarQuarterEnd();
        var indicatorSignals = await db.IndicatorSignals
            .Include(x => x.FiscalQuarter)
            .Where(x => x.FiscalQuarter != null && x.FiscalQuarter.PeriodEnd <= asOfPeriodEnd)
            .ToListAsync(cancellationToken);
        var latestIndicatorSignals = indicatorSignals
            .GroupBy(x => new { x.Category, x.CompanyId })
            .SelectMany(x => x.OrderByDescending(v => v.FiscalQuarter!.PeriodEnd).Take(1))
            .Select(x => new CategorySignal(x.Category, x.ScoreImpact, x.Summary))
            .ToList();
        var transcriptSignals = await GetTranscriptDerivedSignalsAsync(asOfPeriodEnd, cancellationToken);
        var metricSignals = await GetMetricDerivedSignalsAsync(asOfPeriodEnd, cancellationToken);
        var signals = latestIndicatorSignals.Concat(transcriptSignals).Concat(metricSignals).ToList();
        var signalsByCategory = signals
            .GroupBy(x => x.Category)
            .ToDictionary(x => x.Key, x => x.ToList());

        return Enum.GetValues<RiskScoreCategory>()
            .Select(category =>
            {
                if (!signalsByCategory.TryGetValue(category, out var categorySignals) || categorySignals.Count == 0)
                {
                    return new CategoryStatusDto(category, 0, "No data yet", "No real signals imported for this category yet.");
                }

                var average = Math.Round(categorySignals.Average(v => v.ScoreImpact), 1);
                return new CategoryStatusDto(
                    category,
                    average,
                    average < -25 ? "Weakening" : average > 15 ? "Constructive" : "Mixed",
                    TextSanitizer.ToPlainText(categorySignals.OrderByDescending(v => Math.Abs(v.ScoreImpact)).Select(v => v.Summary).First()));
            })
            .OrderByDescending(x => x.Status != "No data yet")
            .ThenByDescending(x => Math.Abs(x.AverageSignal))
            .ThenBy(x => x.Category.ToString())
            .ToList();
    }

    private async Task<IReadOnlyList<CategorySignal>> GetTranscriptDerivedSignalsAsync(DateOnly asOfPeriodEnd, CancellationToken cancellationToken)
    {
        var mentions = await db.TranscriptMentions
            .Include(x => x.Transcript)!.ThenInclude(x => x!.FiscalQuarter)
            .ToListAsync(cancellationToken);
        var latestTranscriptIds = mentions
            .Where(x => x.Transcript is not null && TranscriptPeriodEnd(x.Transcript) <= asOfPeriodEnd)
            .GroupBy(x => x.Transcript!.CompanyId)
            .Select(x => x
                .OrderByDescending(v => TranscriptPeriodEnd(v.Transcript!))
                .ThenByDescending(v => v.Transcript!.ImportedAtUtc)
                .First()
                .TranscriptId)
            .ToHashSet();

        return mentions
            .Where(x => latestTranscriptIds.Contains(x.TranscriptId))
            .GroupBy(x => MapTranscriptGroup(x.KeywordGroup))
            .Select(group =>
            {
                var strongestMention = group
                    .OrderByDescending(x => x.Count)
                    .First();
                var summary = strongestMention.ContextSnippet ??
                    $"{strongestMention.KeywordGroup} transcript mentions from {strongestMention.Transcript?.Title ?? "imported transcript"}.";
                return new CategorySignal(group.Key, Math.Round(group.Average(x => x.SentimentScore), 1), summary);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<CategorySignal>> GetMetricDerivedSignalsAsync(DateOnly asOfPeriodEnd, CancellationToken cancellationToken)
    {
        var metrics = await db.FinancialMetrics
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .ToListAsync(cancellationToken);
        metrics = metrics
            .Where(x => MetricPeriodEnd(x) <= asOfPeriodEnd)
            .GroupBy(x => new { x.CompanyId, Metric = x.MetricName ?? x.Kind.ToString() })
            .Select(x => x.OrderByDescending(MetricPeriodEnd).First())
            .ToList();
        var signals = new List<CategorySignal>();

        var capexRatios = metrics
            .Where(x => x.Kind == MetricKind.CapexAsPercentOfOperatingCashFlow || x.MetricName == "Capex / OCF")
            .Select(x => x.Value)
            .ToList();
        if (capexRatios.Count > 0)
        {
            var averageRatio = capexRatios.Average();
            signals.Add(new CategorySignal(
                RiskScoreCategory.FinancialStressFreeCashFlow,
                Math.Round(Math.Clamp((50m - averageRatio) * 2m, -80m, 40m), 1),
                $"Average capex/OCF is {averageRatio:0.0}%."));
        }

        var hyperscalerGrowth = metrics
            .Where(x => x.Company?.IsHyperscaler == true &&
                (x.Kind == MetricKind.CapexQoqGrowth || x.Kind == MetricKind.CapexYoyGrowth || x.MetricName is "Capex QoQ Growth" or "Capex YoY Growth"))
            .Select(x => x.Value)
            .ToList();
        if (hyperscalerGrowth.Count > 0)
        {
            var averageGrowth = hyperscalerGrowth.Average();
            signals.Add(new CategorySignal(
                RiskScoreCategory.HyperscalerCapexRevisionTrend,
                Math.Round(Math.Clamp(averageGrowth, -70m, 60m), 1),
                $"Average hyperscaler capex growth is {averageGrowth:0.0}%."));
        }

        return signals;
    }

    private static RiskScoreCategory MapTranscriptGroup(string group) => group switch
    {
        "Memory/HBM" => RiskScoreCategory.HbmDramPricingAllocation,
        "Packaging" => RiskScoreCategory.CowosAdvancedPackaging,
        "Power" => RiskScoreCategory.DataCenterPower,
        "Capex" => RiskScoreCategory.HyperscalerCapexRevisionTrend,
        "Financial stress" => RiskScoreCategory.FinancialStressFreeCashFlow,
        _ => RiskScoreCategory.AiRevenueMonetization
    };

    public async Task<IReadOnlyList<TranscriptSignalDto>> GetTranscriptSignalsAsync(CancellationToken cancellationToken = default)
    {
        return await db.TranscriptMentions
            .Include(x => x.Transcript)!.ThenInclude(x => x!.Company)
            .Include(x => x.Transcript)!.ThenInclude(x => x!.FiscalQuarter)
            .OrderByDescending(x => x.Count)
            .Select(x => new TranscriptSignalDto(
                x.Transcript!.Company!.Ticker,
                x.Transcript.FiscalQuarter!.Label,
                x.Transcript.Title,
                x.KeywordGroup,
                x.Count,
                x.Transcript.PublishedDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuarterScoreDto>> GetRiskScoreHistoryAsync(CancellationToken cancellationToken = default)
    {
        var asOfPeriodEnd = CurrentCalendarQuarterEnd();
        var snapshots = await db.RiskScoreSnapshots
            .Include(x => x.FiscalQuarter)
            .Where(x => x.FiscalQuarter != null && x.FiscalQuarter.PeriodEnd <= asOfPeriodEnd)
            .OrderByDescending(x => x.FiscalQuarter!.PeriodEnd)
            .Take(8)
            .OrderBy(x => x.FiscalQuarter!.PeriodEnd)
            .Select(x => new QuarterScoreDto(x.FiscalQuarter!.Label, x.Score, x.ChangeFromPreviousQuarter, x.Band))
            .ToListAsync(cancellationToken);
        return snapshots;
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        var alerts = await db.WatchlistAlerts.ToListAsync(cancellationToken);
        return alerts
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AlertDto(x.Id, x.Severity, x.Title, x.Message, x.CreatedAt, x.IsAcknowledged))
            .ToList();
    }

    public async Task<SignalDto> AddManualEntryAsync(ManualEntryRequest request, CancellationToken cancellationToken = default)
    {
        var ticker = request.Ticker.ToUpper();
        var company = await db.Companies.SingleAsync(x => x.Ticker == ticker, cancellationToken);
        var asOfPeriodEnd = CurrentCalendarQuarterEnd();
        var quarter = await db.FiscalQuarters
            .Where(x => x.PeriodEnd <= asOfPeriodEnd)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Quarter)
            .FirstAsync(cancellationToken);
        var category = Enum.TryParse<RiskScoreCategory>(request.Category, true, out var parsed)
            ? parsed
            : RiskScoreCategory.FinancialStressFreeCashFlow;

        var direction = request.ScoreImpact switch
        {
            > 5 => SignalDirection.Bullish,
            < -5 => SignalDirection.Bearish,
            _ => SignalDirection.Neutral
        };

        var signal = new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = category,
            Name = request.SourceTitle,
            Direction = direction,
            ScoreImpact = request.ScoreImpact,
            Summary = request.Summary
        };

        db.IndicatorSignals.Add(signal);
        db.SourceDocuments.Add(new SourceDocument
        {
            CompanyId = company.Id,
            SourceType = SourceType.ManualEntry,
            Title = request.SourceTitle,
            Summary = request.Summary,
            Url = "",
            PublishedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        await db.SaveChangesAsync(cancellationToken);

        return new SignalDto(company.Ticker, quarter.Label, category, signal.Name, direction, signal.ScoreImpact, signal.Summary);
    }

    private IQueryable<SignalDto> SignalQuery()
    {
        return db.IndicatorSignals
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                x.Direction,
                x.ScoreImpact,
                x.Summary));
    }

    private IQueryable<SourceDocumentDto> SourceQuery()
    {
        return db.SourceDocuments
            .Include(x => x.Company)
            .Select(x => new SourceDocumentDto(
                x.Company == null ? null : x.Company.Ticker,
                x.SourceType,
                x.Title,
                x.Url,
                x.Summary,
                x.PublishedDate));
    }

    private static string BuildSignalSummary(IReadOnlyList<SignalDto> signals, string emptySummary)
    {
        if (signals.Count == 0)
        {
            return emptySummary;
        }

        return string.Join(" ", signals.Take(3).Select(x => TextSanitizer.ToPlainText(x.Summary)));
    }

    private static IEnumerable<SignalDto> BuildCategoryDashboardSignals(IReadOnlyList<CategoryStatusDto> categories, string quarter)
    {
        return categories
            .Where(x => x.AverageSignal != 0)
            .Select(x => new SignalDto(
                null,
                quarter,
                x.Category,
                $"{FormatCategoryName(x.Category)} derived signal",
                x.AverageSignal > 0 ? SignalDirection.Bullish : SignalDirection.Bearish,
                x.AverageSignal,
                x.Summary));
    }

    private static SignalDirection DirectionFromImpact(decimal impact) => impact switch
    {
        > 5 => SignalDirection.Bullish,
        < -5 => SignalDirection.Bearish,
        _ => SignalDirection.Neutral
    };

    private static string SignalQuarter(FiscalQuarter? quarter, string? sourcePeriodLabel) =>
        !string.IsNullOrWhiteSpace(sourcePeriodLabel) ? sourcePeriodLabel : quarter?.Label ?? "Latest";

    private static string FormatCategoryName(RiskScoreCategory category) => category switch
    {
        RiskScoreCategory.HyperscalerCapexRevisionTrend => "Hyperscaler capex",
        RiskScoreCategory.HbmDramPricingAllocation => "HBM/DRAM",
        RiskScoreCategory.CowosAdvancedPackaging => "CoWoS/packaging",
        RiskScoreCategory.DataCenterPower => "Data center/power",
        RiskScoreCategory.AiRevenueMonetization => "AI revenue",
        RiskScoreCategory.FinancialStressFreeCashFlow => "Financial stress/FCF",
        _ => category.ToString()
    };

    private static DateOnly CurrentCalendarQuarterEnd()
    {
        var today = DateTime.UtcNow;
        var quarter = ((today.Month - 1) / 3) + 1;
        return new DateOnly(today.Year, quarter * 3, DateTime.DaysInMonth(today.Year, quarter * 3));
    }

    private static DateOnly MetricPeriodEnd(FinancialMetric metric) => metric.PeriodEndDate ?? metric.FiscalQuarter?.PeriodEnd ?? DateOnly.MinValue;

    private static DateOnly TranscriptPeriodEnd(Transcript transcript) => transcript.PeriodEndDate ?? transcript.CallDate ?? transcript.FiscalQuarter?.PeriodEnd ?? transcript.PublishedDate;

    private sealed record CategorySignal(RiskScoreCategory Category, decimal ScoreImpact, string Summary);
}
