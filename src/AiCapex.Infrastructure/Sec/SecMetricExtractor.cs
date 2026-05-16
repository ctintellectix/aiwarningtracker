namespace AiCapex.Infrastructure.Sec;

public static class SecMetricExtractor
{
    public static IReadOnlyList<SecExtractedMetric> Extract(IEnumerable<SecFactValue> facts)
    {
        var quarterlyFacts = facts
            .Where(x => x.FiscalPeriod.StartsWith("Q", StringComparison.OrdinalIgnoreCase) && TryQuarter(x.FiscalPeriod, out _))
            .GroupBy(x => new { x.FiscalYear, Quarter = Quarter(x.FiscalPeriod), x.Unit })
            .OrderBy(x => x.Key.FiscalYear)
            .ThenBy(x => x.Key.Quarter)
            .ToList();

        var metrics = new List<SecExtractedMetric>();
        var priorCapexFacts = new Dictionary<(int FiscalYear, string Unit), SecFactValue>();
        var priorOperatingCashFlowFacts = new Dictionary<(int FiscalYear, string Unit), SecFactValue>();
        foreach (var group in quarterlyFacts)
        {
            var sourceUrl = group.First().SourceUrl;
            var periodEndDate = group.Select(x => x.EndDate).Where(x => x.HasValue).Max();
            var capex = FirstDurationFact(group,
                "PaymentsToAcquirePropertyPlantAndEquipment",
                "PaymentsToAcquireProductiveAssets",
                "CapitalExpendituresIncurredButNotYetPaid",
                "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities");
            var ocf = FirstDurationFact(group,
                "NetCashProvidedByUsedInOperatingActivities",
                "CashFlowsFromUsedInOperations");
            var revenue = FirstDurationFact(group,
                "Revenues",
                "SalesRevenueNet",
                "RevenueFromContractWithCustomerExcludingAssessedTax",
                "Revenue");
            var debt = FirstInstantFact(group,
                "LongTermDebt",
                "LongTermDebtCurrent",
                "ShortTermBorrowings",
                "Borrowings",
                "CurrentBorrowings",
                "NoncurrentBorrowings");
            var key = (group.Key.FiscalYear, group.Key.Unit);
            var quarterlyCapex = QuarterlyCashFlowValue(capex, priorCapexFacts.GetValueOrDefault(key), group.Key.Quarter);
            var quarterlyOcf = QuarterlyCashFlowValue(ocf, priorOperatingCashFlowFacts.GetValueOrDefault(key), group.Key.Quarter);

            if (quarterlyCapex is not null)
            {
                metrics.Add(new SecExtractedMetric("Quarterly Capex", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, Math.Abs(quarterlyCapex.Value), group.Key.Unit, sourceUrl));
            }

            if (quarterlyOcf is not null)
            {
                metrics.Add(new SecExtractedMetric("Operating Cash Flow", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, quarterlyOcf.Value, group.Key.Unit, sourceUrl));
            }

            if (quarterlyCapex is not null && quarterlyOcf is not null && quarterlyOcf.Value != 0)
            {
                metrics.Add(new SecExtractedMetric("Capex / OCF", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, Math.Round(Math.Abs(quarterlyCapex.Value) / Math.Abs(quarterlyOcf.Value) * 100, 2), "%", sourceUrl));
            }

            if (revenue is not null)
            {
                metrics.Add(new SecExtractedMetric("Revenue", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, revenue.Value, group.Key.Unit, sourceUrl));
            }

            if (debt is not null)
            {
                metrics.Add(new SecExtractedMetric("Debt", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, debt.Value, group.Key.Unit, sourceUrl));
            }

            if (capex is not null)
            {
                priorCapexFacts[key] = capex;
            }

            if (ocf is not null)
            {
                priorOperatingCashFlowFacts[key] = ocf;
            }
        }

        AddSyntheticQ4Metrics(metrics, facts);
        AddGrowthMetrics(metrics, "Quarterly Capex");
        AddTtmCapex(metrics);
        return metrics;
    }

    private static SecFactValue? FirstDurationFact(IEnumerable<SecFactValue> facts, params string[] tags) =>
        facts.Where(x => tags.Contains(x.Tag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => TaxonomyPriority(x.Taxonomy))
            .ThenByDescending(x => x.EndDate)
            .ThenBy(x => DurationDays(x))
            .ThenByDescending(x => x.FiledDate)
            .FirstOrDefault();

    private static SecFactValue? FirstInstantFact(IEnumerable<SecFactValue> facts, params string[] tags) =>
        facts.Where(x => tags.Contains(x.Tag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => TaxonomyPriority(x.Taxonomy))
            .ThenByDescending(x => x.EndDate)
            .ThenByDescending(x => x.FiledDate)
            .FirstOrDefault();

    private static int TaxonomyPriority(string taxonomy) =>
        taxonomy.Equals("us-gaap", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static int DurationDays(SecFactValue fact) =>
        fact.StartDate is not null && fact.EndDate is not null
            ? fact.EndDate.Value.DayNumber - fact.StartDate.Value.DayNumber
            : int.MaxValue;

    private static decimal? QuarterlyCashFlowValue(SecFactValue? current, SecFactValue? prior, int quarter)
    {
        if (current is null)
        {
            return null;
        }

        // SEC interim cash-flow facts are commonly year-to-date values. Convert
        // Q2-Q4 cumulative values into standalone quarters before storing them.
        return quarter > 1 && prior is not null
            ? current.Value - prior.Value
            : current.Value;
    }

    private static void AddSyntheticQ4Metrics(List<SecExtractedMetric> metrics, IEnumerable<SecFactValue> facts)
    {
        var annualFacts = facts
            .Where(x => x.FiscalPeriod.Equals("FY", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => new { x.FiscalYear, x.Unit })
            .ToList();

        foreach (var annualGroup in annualFacts)
        {
            if (metrics.Any(x =>
                x.FiscalYear == annualGroup.Key.FiscalYear &&
                x.FiscalQuarter == 4 &&
                x.Unit == annualGroup.Key.Unit))
            {
                continue;
            }

            var annualRevenue = FirstDurationFact(annualGroup, "Revenues", "SalesRevenueNet", "RevenueFromContractWithCustomerExcludingAssessedTax", "Revenue");
            var annualCapex = FirstDurationFact(
                annualGroup,
                "PaymentsToAcquirePropertyPlantAndEquipment",
                "PaymentsToAcquireProductiveAssets",
                "CapitalExpendituresIncurredButNotYetPaid",
                "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities");
            var annualOcf = FirstDurationFact(
                annualGroup,
                "NetCashProvidedByUsedInOperatingActivities",
                "CashFlowsFromUsedInOperations");
            var sourceUrl = annualGroup.First().SourceUrl;
            var periodEndDate = annualGroup.Select(x => x.EndDate).Where(x => x.HasValue).Max();

            var q1ToQ3 = metrics
                .Where(x => x.FiscalYear == annualGroup.Key.FiscalYear &&
                    x.FiscalQuarter is >= 1 and <= 3 &&
                    x.Unit == annualGroup.Key.Unit)
                .ToList();
            AddSyntheticFlowMetric(metrics, annualRevenue, q1ToQ3, "Revenue", annualGroup.Key.FiscalYear, periodEndDate, sourceUrl);
            AddSyntheticFlowMetric(metrics, annualCapex, q1ToQ3, "Quarterly Capex", annualGroup.Key.FiscalYear, periodEndDate, sourceUrl, absoluteValue: true);
            AddSyntheticFlowMetric(metrics, annualOcf, q1ToQ3, "Operating Cash Flow", annualGroup.Key.FiscalYear, periodEndDate, sourceUrl);

            var q4Capex = metrics.FirstOrDefault(x =>
                x.FiscalYear == annualGroup.Key.FiscalYear &&
                x.FiscalQuarter == 4 &&
                x.MetricName == "Quarterly Capex" &&
                x.Unit == annualGroup.Key.Unit);
            var q4Ocf = metrics.FirstOrDefault(x =>
                x.FiscalYear == annualGroup.Key.FiscalYear &&
                x.FiscalQuarter == 4 &&
                x.MetricName == "Operating Cash Flow" &&
                x.Unit == annualGroup.Key.Unit);
            if (q4Capex is not null && q4Ocf is not null && q4Ocf.Value != 0)
            {
                metrics.Add(q4Capex with
                {
                    MetricName = "Capex / OCF",
                    Unit = "%",
                    Value = Math.Round(Math.Abs(q4Capex.Value) / Math.Abs(q4Ocf.Value) * 100, 2)
                });
            }
        }
    }

    private static void AddSyntheticFlowMetric(
        List<SecExtractedMetric> metrics,
        SecFactValue? annualFact,
        IReadOnlyList<SecExtractedMetric> q1ToQ3,
        string metricName,
        int fiscalYear,
        DateOnly? periodEndDate,
        string sourceUrl,
        bool absoluteValue = false)
    {
        if (annualFact is null ||
            metrics.Any(x => x.FiscalYear == fiscalYear && x.FiscalQuarter == 4 && x.MetricName == metricName && x.Unit == annualFact.Unit))
        {
            return;
        }

        var priorQuarterValues = q1ToQ3
            .Where(x => x.MetricName == metricName)
            .OrderBy(x => x.FiscalQuarter)
            .ToList();
        if (priorQuarterValues.Count != 3)
        {
            return;
        }

        var q4Value = annualFact.Value - priorQuarterValues.Sum(x => x.Value);
        metrics.Add(new SecExtractedMetric(
            metricName,
            fiscalYear,
            4,
            periodEndDate,
            absoluteValue ? Math.Abs(q4Value) : q4Value,
            annualFact.Unit,
            sourceUrl));
    }

    private static bool TryQuarter(string fiscalPeriod, out int quarter) =>
        int.TryParse(fiscalPeriod.TrimStart('Q', 'q'), out quarter) && quarter is >= 1 and <= 4;

    private static int Quarter(string fiscalPeriod) => TryQuarter(fiscalPeriod, out var quarter) ? quarter : 0;

    private static void AddGrowthMetrics(List<SecExtractedMetric> metrics, string sourceMetric)
    {
        var capex = metrics.Where(x => x.MetricName == sourceMetric).OrderBy(x => x.FiscalYear).ThenBy(x => x.FiscalQuarter).ToList();
        foreach (var current in capex)
        {
            var previousQuarter = capex.LastOrDefault(x =>
                x.FiscalYear < current.FiscalYear ||
                x.FiscalYear == current.FiscalYear && x.FiscalQuarter < current.FiscalQuarter);
            if (previousQuarter is not null && previousQuarter.Value != 0)
            {
                metrics.Add(current with { MetricName = "Capex QoQ Growth", Unit = "%", Value = Math.Round((current.Value - previousQuarter.Value) / Math.Abs(previousQuarter.Value) * 100, 2) });
            }

            var previousYear = capex.FirstOrDefault(x => x.FiscalYear == current.FiscalYear - 1 && x.FiscalQuarter == current.FiscalQuarter);
            if (previousYear is not null && previousYear.Value != 0)
            {
                metrics.Add(current with { MetricName = "Capex YoY Growth", Unit = "%", Value = Math.Round((current.Value - previousYear.Value) / Math.Abs(previousYear.Value) * 100, 2) });
            }
        }
    }

    private static void AddTtmCapex(List<SecExtractedMetric> metrics)
    {
        var capex = metrics.Where(x => x.MetricName == "Quarterly Capex").OrderBy(x => x.FiscalYear).ThenBy(x => x.FiscalQuarter).ToList();
        for (var i = 3; i < capex.Count; i++)
        {
            var window = capex.Skip(i - 3).Take(4).ToList();
            metrics.Add(capex[i] with { MetricName = "TTM Capex", Value = window.Sum(x => x.Value) });
        }
    }
}
