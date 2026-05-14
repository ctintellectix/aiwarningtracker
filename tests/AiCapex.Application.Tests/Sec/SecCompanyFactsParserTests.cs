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
}
