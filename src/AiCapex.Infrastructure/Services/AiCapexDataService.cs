using AiCapex.Application.Dashboard;
using AiCapex.Application.Scoring;
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
        var latest = history.LastOrDefault() ?? new QuarterScoreDto("No score yet", 50, 0, "Neutral");
        var categories = await GetIndicatorTrendsAsync(cancellationToken);
        var signals = BuildCategoryDashboardSignals(categories, latest.Quarter).ToList();
        var bullishSignals = signals.Where(x => x.ScoreImpact > 0).OrderByDescending(x => x.ScoreImpact).Take(4).ToList();
        var bearishSignals = signals.Where(x => x.ScoreImpact < 0).OrderBy(x => x.ScoreImpact).Take(4).ToList();
        var topCompanyDrivers = await GetTopCompanyDriversAsync(cancellationToken);
        var bearishSummary = BuildSignalSummary(bearishSignals, "No bearish real-data signals imported yet.");

        return new DashboardSummaryDto(
            latest.Score,
            latest.Change ?? 0,
            latest.Band,
            BuildSignalSummary(bullishSignals, "No bullish real-data signals imported yet."),
            bearishSummary,
            bullishSignals,
            bearishSignals,
            topCompanyDrivers,
            categories,
            history);
    }

    public async Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var companies = await db.Companies
            .OrderBy(x => x.Ticker)
            .ToListAsync(cancellationToken);
        var result = new List<CompanyDto>(companies.Count);

        foreach (var company in companies)
        {
            var signals = await GetCurrentCompanyScoreSignalsAsync(company, cancellationToken);
            var latestMomentumSignal = signals.Count == 0 ? 0 : Math.Round(AggregateCompanySignals(signals), 1);
            result.Add(new CompanyDto(company.Id, company.Ticker, company.Name, company.Segment, (double)SignalScoreInterpreter.ToDisplaySignal(latestMomentumSignal)));
        }

        return result;
    }

    public async Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Ticker == ticker.ToUpper(), cancellationToken);
        if (company is null)
        {
            return null;
        }

        var metrics = await GetCompanyMetricsAsync(company.Ticker, cancellationToken);
        var signals = (await GetCompanySignalsAsync(company, cancellationToken))
            .Where(x => !IsKeywordTranscriptSignal(x))
            .ToList();
        var currentScoreSignals = (await GetCurrentCompanyScoreSignalsAsync(company, cancellationToken))
            .Where(x => !IsKeywordTranscriptSignal(x))
            .ToList();
        var currentSignals = currentScoreSignals
            .Select(ToDisplaySignalDto)
            .ToList();
        var historicalSignals = signals
            .Where(signal => !currentSignals.Any(current => SameSignal(current, signal)))
            .ToList();
        var latestMomentumSignal = currentScoreSignals.Count == 0 ? 0 : Math.Round(AggregateCompanySignals(currentScoreSignals), 1);
        var dto = new CompanyDto(company.Id, company.Ticker, company.Name, company.Segment, (double)SignalScoreInterpreter.ToDisplaySignal(latestMomentumSignal));

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

        return new CompanyDetailDto(dto, metrics, signals, currentSignals, historicalSignals, sources);
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
            var impact = DerivedFinancialSignalScorer.ScoreCapexOcf(capexRatio.Value);
            impact = await AdjustCapexStressForSemiconductorDemandAsync(company, impact, cancellationToken);
            signals.Add(new SignalDto(
                company.Ticker,
                SignalQuarter(latestMetricQuarter?.FiscalQuarter, latestMetricQuarter?.SourcePeriodLabel),
                RiskScoreCategory.FinancialStressFreeCashFlow,
                "Capex / OCF derived signal",
                DirectionFromImpact(impact),
                impact,
                $"Latest capex/OCF is {capexRatio.Value:0.0}%.",
                "Derived financial metric"));
        }

        var capexGrowthValues = latestMetrics
            .Where(x => x.Kind == MetricKind.CapexQoqGrowth || x.Kind == MetricKind.CapexYoyGrowth || x.MetricName is "Capex QoQ Growth" or "Capex YoY Growth")
            .Select(x => x.Value)
            .ToList();
        if (company.IsHyperscaler && capexGrowthValues.Count > 0)
        {
            var averageGrowth = capexGrowthValues.Average();
            var impact = DerivedFinancialSignalScorer.ScoreCapexGrowth(averageGrowth);
            signals.Add(new SignalDto(
                company.Ticker,
                SignalQuarter(latestMetricQuarter?.FiscalQuarter, latestMetricQuarter?.SourcePeriodLabel),
                RiskScoreCategory.HyperscalerCapexRevisionTrend,
                "Capex growth derived signal",
                DirectionFromImpact(impact),
                impact,
                $"Latest capex growth average is {averageGrowth:0.0}%.",
                "Derived financial metric"));
        }

        return signals
            .OrderByDescending(x => Math.Abs(x.ScoreImpact))
            .ThenBy(x => x.Category.ToString())
            .ToList();
    }

    private async Task<decimal> AdjustCapexStressForSemiconductorDemandAsync(Company company, decimal impact, CancellationToken cancellationToken)
    {
        if (!company.IsSemiconductor || impact >= 0)
        {
            return impact;
        }

        var asOfPeriodEnd = CurrentCalendarQuarterEnd();
        var latestSignals = (await db.IndicatorSignals
                .Include(x => x.FiscalQuarter)
                .Where(x => x.CompanyId == company.Id &&
                    x.FiscalQuarter != null &&
                    x.FiscalQuarter.PeriodEnd <= asOfPeriodEnd)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.Category)
            .Select(group => group
                .OrderByDescending(x => x.FiscalQuarter!.PeriodEnd)
                .First())
            .ToList();

        var hbm = latestSignals.FirstOrDefault(x => x.Category == RiskScoreCategory.HbmDramPricingAllocation);
        var monetization = latestSignals.FirstOrDefault(x => x.Category == RiskScoreCategory.AiRevenueMonetization);
        var financialStress = latestSignals.FirstOrDefault(x => x.Category == RiskScoreCategory.FinancialStressFreeCashFlow);
        var demandSupport = new[] { hbm, monetization, financialStress }
            .Where(x => x is not null)
            .Select(x => SignalScoreInterpreter.ToScoringSignal(x!.ScoreImpact, x.SignalName))
            .ToList();

        if (demandSupport.Count < 3 || demandSupport.Average() < 3.5m)
        {
            return impact;
        }

        // Semiconductor expansions can temporarily run above OCF during a genuine
        // demand-led HBM buildout. Keep the warning visible, but do not treat it
        // like pure distress when current demand and cash commentary are strong.
        return Math.Max(impact, -2.5m);
    }

    private async Task<IReadOnlyList<SignalDto>> GetCompanySignalsAsync(Company company, CancellationToken cancellationToken)
    {
        var signals = await db.IndicatorSignals
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Include(x => x.SourceDocument)
            .Where(x => x.CompanyId == company.Id)
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                DisplayDirection(x.ScoreImpact, x.SignalName, x.Direction),
                SignalScoreInterpreter.ToScoringSignal(x.ScoreImpact, x.SignalName),
                x.Summary,
                SourceLabel(x.SourceDocument)))
            .ToListAsync(cancellationToken);
        signals.AddRange(await GetCompanyDerivedSignalsAsync(company, cancellationToken));
        return signals.Select(ToDisplaySignalDto).ToList();
    }

    private async Task<IReadOnlyList<SignalDto>> GetCurrentCompanyScoreSignalsAsync(Company company, CancellationToken cancellationToken)
    {
        var asOfPeriodEnd = CurrentCalendarQuarterEnd();
        var storedSignals = await db.IndicatorSignals
                .Include(x => x.Company)
                .Include(x => x.FiscalQuarter)
                .Include(x => x.SourceDocument)
                .Where(x => x.CompanyId == company.Id &&
                    x.FiscalQuarter != null &&
                    x.FiscalQuarter.PeriodEnd <= asOfPeriodEnd)
                .ToListAsync(cancellationToken);
        var latestStoredSignals = storedSignals
            .GroupBy(x => x.Category)
            .Select(group => group
                .OrderByDescending(x => x.FiscalQuarter!.PeriodEnd)
                .ThenByDescending(x => Math.Abs(x.ScoreImpact))
                .First())
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                DisplayDirection(x.ScoreImpact, x.SignalName, x.Direction),
                SignalScoreInterpreter.ToScoringSignal(x.ScoreImpact, x.SignalName),
                x.Summary,
                SourceLabel(x.SourceDocument)))
            .ToList();

        var derivedSignals = (await GetCompanyDerivedSignalsAsync(company, cancellationToken)).ToList();
        var directionalCategories = latestStoredSignals
            .Where(x => x.ScoreImpact != 0)
            .Select(x => x.Category)
            .ToHashSet();
        derivedSignals.RemoveAll(x =>
            x.ScoreImpact == 0 &&
            x.Name.EndsWith("transcript signal", StringComparison.OrdinalIgnoreCase) &&
            directionalCategories.Contains(x.Category));

        return latestStoredSignals
            .Concat(derivedSignals)
            .ToList();
    }

    private async Task<IReadOnlyList<SignalDto>> GetTopCompanyDriversAsync(CancellationToken cancellationToken)
    {
        var companies = await db.Companies
            .OrderBy(x => x.Ticker)
            .ToListAsync(cancellationToken);
        var signals = new List<SignalDto>();

        foreach (var company in companies)
        {
            signals.AddRange((await GetCurrentCompanyScoreSignalsAsync(company, cancellationToken))
                .Where(x => x.ScoreImpact != 0 && !IsKeywordTranscriptSignal(x)));
        }

        return signals
            .Select(ToDisplaySignalDto)
            .OrderByDescending(x => Math.Abs(x.ScoreImpact))
            .ThenBy(x => x.Ticker)
            .ThenBy(x => x.Category.ToString())
            .Take(5)
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
            .Where(x => x.FiscalQuarter != null &&
                x.FiscalQuarter.PeriodEnd <= asOfPeriodEnd)
            .ToListAsync(cancellationToken);
        var latestIndicatorSignals = indicatorSignals
            .GroupBy(x => new { x.Category, x.CompanyId })
            .SelectMany(x => x.OrderByDescending(v => v.FiscalQuarter!.PeriodEnd).Take(1))
            .Select(x => new CategorySignal(
                x.Category,
                SignalScoreInterpreter.ToScoringSignal(x.ScoreImpact, x.SignalName),
                x.Confidence,
                x.Summary))
            .ToList();
        var metricSignals = await GetMetricDerivedSignalsAsync(asOfPeriodEnd, cancellationToken);
        var signals = latestIndicatorSignals.Concat(metricSignals).ToList();
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

                var average = CategorySignalAggregator.Aggregate(categorySignals.Select(v => (v.ScoreImpact, v.Confidence)));
                var displayAverage = SignalScoreInterpreter.ToDisplaySignal(average);
                return new CategoryStatusDto(
                    category,
                    displayAverage,
                    CategoryLabel(displayAverage),
                    TextSanitizer.ToPlainText(SelectCategorySummary(categorySignals, average)));
            })
            .OrderByDescending(x => x.Status != "No data yet")
            .ThenByDescending(x => Math.Abs(x.AverageSignal))
            .ThenBy(x => x.Category.ToString())
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
                DerivedFinancialSignalScorer.ScoreCapexOcf(averageRatio),
                80,
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
                DerivedFinancialSignalScorer.ScoreCapexGrowth(averageGrowth),
                85,
                $"Average hyperscaler capex growth is {averageGrowth:0.0}%."));
        }

        return signals;
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
            .Select(x => new
            {
                x.FiscalQuarter!.Year,
                x.FiscalQuarter.Quarter,
                x.FiscalQuarter.PeriodEnd,
                x.Score,
                x.ChangeFromPreviousQuarter,
                x.Band
            })
            .ToListAsync(cancellationToken);

        var result = snapshots
            .Select(x => new QuarterScoreDto($"Q{x.Quarter} {x.Year}", x.Score, x.ChangeFromPreviousQuarter, x.Band))
            .ToList();

        var firstSnapshot = snapshots.FirstOrDefault();
        if (firstSnapshot is not null)
        {
            var hasPriorSnapshot = await db.RiskScoreSnapshots
                .Include(x => x.FiscalQuarter)
                .AnyAsync(x => x.FiscalQuarter != null && x.FiscalQuarter.PeriodEnd < firstSnapshot.PeriodEnd, cancellationToken);
            if (!hasPriorSnapshot)
            {
                result[0] = result[0] with { Change = null };
            }
        }

        return result;
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
            > 1 => SignalDirection.Bullish,
            < -1 => SignalDirection.Bearish,
            _ => SignalDirection.Neutral
        };

        var signal = new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = category,
            Name = request.SourceTitle,
            Direction = direction,
            ScoreImpact = SignalScoreInterpreter.FromDisplaySignal(request.ScoreImpact),
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

        return ToDisplaySignalDto(new SignalDto(company.Ticker, quarter.Label, category, signal.Name, direction, signal.ScoreImpact, signal.Summary, "Manual entry"));
    }

    private IQueryable<SignalDto> SignalQuery()
    {
        return db.IndicatorSignals
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Include(x => x.SourceDocument)
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                DisplayDirection(x.ScoreImpact, x.SignalName, x.Direction),
                SignalScoreInterpreter.ToScoringSignal(x.ScoreImpact, x.SignalName),
                x.Summary,
                SourceLabel(x.SourceDocument)));
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

    private static string SelectCategorySummary(IReadOnlyList<CategorySignal> signals, decimal aggregate)
    {
        var matchingSignals = aggregate switch
        {
            > 1 => signals.Where(x => x.ScoreImpact > 1),
            < -1 => signals.Where(x => x.ScoreImpact < -1),
            _ => signals.Where(x => x.ScoreImpact is >= -1 and <= 1)
        };

        var candidates = matchingSignals.Any() ? matchingSignals : signals;
        return candidates
            .OrderByDescending(x => Math.Abs(x.ScoreImpact))
            .Select(x => x.Summary)
            .First();
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
                DirectionFromImpact(x.AverageSignal),
                x.AverageSignal,
                x.Summary,
                "Category rollup"));
    }

    private static SignalDirection DirectionFromImpact(decimal impact) => impact switch
    {
        > 1 => SignalDirection.Bullish,
        < -1 => SignalDirection.Bearish,
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

    private static string SourceLabel(SourceDocument? sourceDocument)
    {
        if (sourceDocument is null)
        {
            return "Imported signal";
        }

        var sourceType = sourceDocument.SourceType switch
        {
            SourceType.SecXbrl => "SEC XBRL",
            SourceType.SecFiling => "SEC filing",
            SourceType.Transcript => "Transcript",
            SourceType.ManualEntry => "Manual entry",
            SourceType.NewsRss => "RSS/news",
            _ => sourceDocument.SourceType.ToString()
        };

        return string.IsNullOrWhiteSpace(sourceDocument.Provider)
            ? sourceType
            : $"{sourceType} - {sourceDocument.Provider}";
    }

    private static string CategoryLabel(decimal signal) => signal switch
    {
        <= -6 => "Very bearish",
        < -1 => "Bearish",
        <= 1 => "Neutral",
        < 6 => "Bullish",
        _ => "Very bullish"
    };

    private static SignalDirection DisplayDirection(decimal rawScore, string? signalName, SignalDirection storedDirection)
    {
        var interpretedScore = SignalScoreInterpreter.ToScoringSignal(rawScore, signalName);
        return interpretedScore switch
        {
            > 1 => SignalDirection.Bullish,
            < -1 => SignalDirection.Bearish,
            _ => SignalDirection.Neutral
        };
    }

    private static decimal AggregateCompanySignals(IReadOnlyList<SignalDto> signals) =>
        CategorySignalAggregator.Aggregate(signals.Select(x => (x.ScoreImpact, 75)));

    private static SignalDto ToDisplaySignalDto(SignalDto signal) =>
        signal with { ScoreImpact = SignalScoreInterpreter.ToDisplaySignal(signal.ScoreImpact) };

    private static bool SameSignal(SignalDto left, SignalDto right) =>
        left.Quarter == right.Quarter &&
        left.Category == right.Category &&
        left.Name == right.Name &&
        left.ScoreImpact == right.ScoreImpact &&
        left.Summary == right.Summary;

    private static bool IsKeywordTranscriptSignal(SignalDto signal) =>
        signal.Name.EndsWith("transcript signal", StringComparison.OrdinalIgnoreCase);

    private static DateOnly CurrentCalendarQuarterEnd()
    {
        var today = DateTime.UtcNow;
        var quarter = ((today.Month - 1) / 3) + 1;
        return new DateOnly(today.Year, quarter * 3, DateTime.DaysInMonth(today.Year, quarter * 3));
    }

    private static DateOnly MetricPeriodEnd(FinancialMetric metric) => metric.PeriodEndDate ?? metric.FiscalQuarter?.PeriodEnd ?? DateOnly.MinValue;

    private sealed record CategorySignal(RiskScoreCategory Category, decimal ScoreImpact, int Confidence, string Summary);
}
