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
        foreach (var group in quarterlyFacts)
        {
            var sourceUrl = group.First().SourceUrl;
            var periodEndDate = group.Select(x => x.EndDate).Where(x => x.HasValue).Max();
            var capex = FirstValue(group,
                "PaymentsToAcquirePropertyPlantAndEquipment",
                "PaymentsToAcquireProductiveAssets",
                "CapitalExpendituresIncurredButNotYetPaid",
                "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities");
            var ocf = FirstValue(group,
                "NetCashProvidedByUsedInOperatingActivities",
                "CashFlowsFromUsedInOperations");
            var revenue = FirstValue(group,
                "Revenues",
                "SalesRevenueNet",
                "Revenue");
            var debt = FirstValue(group,
                "LongTermDebt",
                "LongTermDebtCurrent",
                "ShortTermBorrowings",
                "Borrowings",
                "CurrentBorrowings",
                "NoncurrentBorrowings");

            if (capex is not null)
            {
                metrics.Add(new SecExtractedMetric("Quarterly Capex", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, Math.Abs(capex.Value), group.Key.Unit, sourceUrl));
            }

            if (ocf is not null)
            {
                metrics.Add(new SecExtractedMetric("Operating Cash Flow", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, ocf.Value, group.Key.Unit, sourceUrl));
            }

            if (capex is not null && ocf is not null && ocf.Value != 0)
            {
                metrics.Add(new SecExtractedMetric("Capex / OCF", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, Math.Round(Math.Abs(capex.Value) / Math.Abs(ocf.Value) * 100, 2), "%", sourceUrl));
            }

            if (revenue is not null)
            {
                metrics.Add(new SecExtractedMetric("Revenue", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, revenue.Value, group.Key.Unit, sourceUrl));
            }

            if (debt is not null)
            {
                metrics.Add(new SecExtractedMetric("Debt", group.Key.FiscalYear, group.Key.Quarter, periodEndDate, debt.Value, group.Key.Unit, sourceUrl));
            }
        }

        AddGrowthMetrics(metrics, "Quarterly Capex");
        AddTtmCapex(metrics);
        return metrics;
    }

    private static decimal? FirstValue(IEnumerable<SecFactValue> facts, params string[] tags) =>
        facts.Where(x => tags.Contains(x.Tag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => TaxonomyPriority(x.Taxonomy))
            .ThenByDescending(x => x.FiledDate)
            .Select(x => (decimal?)x.Value)
            .FirstOrDefault();

    private static int TaxonomyPriority(string taxonomy) =>
        taxonomy.Equals("us-gaap", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

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
