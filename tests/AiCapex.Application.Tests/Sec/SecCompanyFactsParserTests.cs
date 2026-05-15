using AiCapex.Infrastructure.Sec;

namespace AiCapex.Application.Tests.Sec;

public class SecCompanyFactsParserTests
{
    [Fact]
    public void Parses_us_gaap_company_facts_for_configured_tags()
    {
        const string json = """
            {
              "cik": 789019,
              "entityName": "MICROSOFT CORPORATION",
              "facts": {
                "us-gaap": {
                  "PaymentsToAcquirePropertyPlantAndEquipment": {
                    "label": "Capital expenditures",
                    "units": {
                      "USD": [
                        { "fy": 2026, "fp": "Q1", "form": "10-Q", "filed": "2025-10-24", "end": "2025-09-30", "val": 12340000000, "accn": "0000789019-25-000123" }
                      ]
                    }
                  },
                  "NetCashProvidedByUsedInOperatingActivities": {
                    "units": {
                      "USD": [
                        { "fy": 2026, "fp": "Q1", "form": "10-Q", "filed": "2025-10-24", "end": "2025-09-30", "val": 30000000000 }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var facts = SecCompanyFactsParser.Parse(json, "https://data.sec.gov/api/xbrl/companyfacts/CIK0000789019.json");

        Assert.Contains(facts, fact => fact.Tag == "PaymentsToAcquirePropertyPlantAndEquipment" && fact.Value == 12340000000m && fact.FiscalPeriod == "Q1");
        Assert.Contains(facts, fact => fact.Tag == "NetCashProvidedByUsedInOperatingActivities" && fact.Unit == "USD");
    }

    [Fact]
    public void Parses_ifrs_company_facts_for_configured_tags()
    {
        const string json = """
            {
              "facts": {
                "ifrs-full": {
                  "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities": {
                    "units": {
                      "EUR": [
                        { "fy": 2025, "fp": "Q1", "form": "6-K", "filed": "2025-04-16", "end": "2025-03-30", "val": -1200000000 }
                      ]
                    }
                  },
                  "CashFlowsFromUsedInOperations": {
                    "units": {
                      "EUR": [
                        { "fy": 2025, "fp": "Q1", "form": "6-K", "filed": "2025-04-16", "end": "2025-03-30", "val": 3000000000 }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var facts = SecCompanyFactsParser.Parse(json, "url");

        Assert.Contains(facts, fact =>
            fact.Taxonomy == "ifrs-full" &&
            fact.Tag == "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities" &&
            fact.Unit == "EUR");
        Assert.Contains(facts, fact =>
            fact.Taxonomy == "ifrs-full" &&
            fact.Tag == "CashFlowsFromUsedInOperations");
    }

    [Fact]
    public void Extracts_financial_metrics_from_company_facts()
    {
        var facts = new[]
        {
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-10-24"), DateOnly.Parse("2025-09-30"), 120m, "url"),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-10-24"), DateOnly.Parse("2025-09-30"), 300m, "url"),
            new SecFactValue("us-gaap", "LongTermDebt", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-10-24"), DateOnly.Parse("2025-09-30"), 80m, "url")
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.Value == 120m);
        Assert.Contains(metrics, metric => metric.MetricName == "Capex / OCF" && metric.Value == 40m);
        Assert.Contains(metrics, metric => metric.MetricName == "Debt" && metric.Value == 80m);
        Assert.All(metrics, metric => Assert.Equal(DateOnly.Parse("2025-09-30"), metric.PeriodEndDate));
    }

    [Fact]
    public void Extracts_normalized_financial_metrics_from_ifrs_company_facts()
    {
        var facts = new[]
        {
            new SecFactValue("ifrs-full", "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities", "EUR", 2025, "Q1", "6-K", DateOnly.Parse("2025-04-16"), DateOnly.Parse("2025-03-30"), -120m, "url"),
            new SecFactValue("ifrs-full", "CashFlowsFromUsedInOperations", "EUR", 2025, "Q1", "6-K", DateOnly.Parse("2025-04-16"), DateOnly.Parse("2025-03-30"), 300m, "url"),
            new SecFactValue("ifrs-full", "Revenue", "EUR", 2025, "Q1", "6-K", DateOnly.Parse("2025-04-16"), DateOnly.Parse("2025-03-30"), 900m, "url"),
            new SecFactValue("ifrs-full", "Borrowings", "EUR", 2025, "Q1", "6-K", DateOnly.Parse("2025-04-16"), DateOnly.Parse("2025-03-30"), 80m, "url")
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.Value == 120m);
        Assert.Contains(metrics, metric => metric.MetricName == "Operating Cash Flow" && metric.Value == 300m);
        Assert.Contains(metrics, metric => metric.MetricName == "Capex / OCF" && metric.Value == 40m);
        Assert.Contains(metrics, metric => metric.MetricName == "Revenue" && metric.Value == 900m);
        Assert.Contains(metrics, metric => metric.MetricName == "Debt" && metric.Value == 80m);
    }

    [Fact]
    public void Prefers_us_gaap_when_same_period_has_us_gaap_and_ifrs_values()
    {
        var facts = new[]
        {
            new SecFactValue("ifrs-full", "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities", "USD", 2025, "Q1", "6-K", DateOnly.Parse("2025-04-16"), DateOnly.Parse("2025-03-30"), -90m, "url"),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2025, "Q1", "10-Q", DateOnly.Parse("2025-04-17"), DateOnly.Parse("2025-03-30"), -120m, "url")
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.Value == 120m);
    }
}
