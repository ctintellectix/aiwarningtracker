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
                        { "fy": 2026, "fp": "Q1", "form": "10-Q", "filed": "2025-10-24", "start": "2025-07-01", "end": "2025-09-30", "val": 12340000000, "accn": "0000789019-25-000123" }
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
        Assert.Contains(facts, fact => fact.Tag == "PaymentsToAcquirePropertyPlantAndEquipment" && fact.StartDate == DateOnly.Parse("2025-07-01"));
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
    public void Extracts_revenue_from_contract_with_customer_tag()
    {
        var facts = new[]
        {
            new SecFactValue("us-gaap", "RevenueFromContractWithCustomerExcludingAssessedTax", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-11-07"), DateOnly.Parse("2025-10-03"), 2308m, "url", null, DateOnly.Parse("2025-06-28"))
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Revenue" && metric.Value == 2308m);
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

    [Fact]
    public void Converts_cumulative_cash_flow_facts_into_quarterly_metrics()
    {
        var facts = new[]
        {
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-06-10"), DateOnly.Parse("2025-05-02"), 568m, "url"),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 1243m, "url"),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q3", "10-Q", DateOnly.Parse("2025-12-09"), DateOnly.Parse("2025-10-31"), 1912m, "url"),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-06-10"), DateOnly.Parse("2025-05-02"), 1000m, "url"),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 2400m, "url"),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q3", "10-Q", DateOnly.Parse("2025-12-09"), DateOnly.Parse("2025-10-31"), 3900m, "url")
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.FiscalQuarter == 1 && metric.Value == 568m);
        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.FiscalQuarter == 2 && metric.Value == 675m);
        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.FiscalQuarter == 3 && metric.Value == 669m);
        Assert.Contains(metrics, metric => metric.MetricName == "Operating Cash Flow" && metric.FiscalQuarter == 2 && metric.Value == 1400m);
        Assert.Contains(metrics, metric => metric.MetricName == "Operating Cash Flow" && metric.FiscalQuarter == 3 && metric.Value == 1500m);
        Assert.Contains(metrics, metric => metric.MetricName == "Capex / OCF" && metric.FiscalQuarter == 2 && metric.Value == 48.21m);
    }

    [Fact]
    public void Prefers_standalone_quarter_revenue_over_ytd_revenue_when_both_exist()
    {
        var facts = new[]
        {
            new SecFactValue("us-gaap", "Revenues", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 29776m, "url", null, DateOnly.Parse("2025-05-03")),
            new SecFactValue("us-gaap", "Revenues", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 53154m, "url", null, DateOnly.Parse("2025-02-01"))
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Revenue" && metric.FiscalQuarter == 2 && metric.Value == 29776m);
    }

    [Fact]
    public void Prefers_latest_period_end_debt_over_comparative_balance_sheet_values()
    {
        var facts = new[]
        {
            new SecFactValue("us-gaap", "LongTermDebt", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-01-31"), 24567m, "url"),
            new SecFactValue("us-gaap", "LongTermDebt", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 28689m, "url")
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Debt" && metric.FiscalQuarter == 2 && metric.Value == 28689m);
    }

    [Fact]
    public void Derives_missing_q4_flow_metrics_from_annual_totals()
    {
        var facts = new[]
        {
            new SecFactValue("us-gaap", "Revenues", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-06-10"), DateOnly.Parse("2025-05-02"), 23378m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "Revenues", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 29776m, "url", null, DateOnly.Parse("2025-05-03")),
            new SecFactValue("us-gaap", "Revenues", "USD", 2026, "Q3", "10-Q", DateOnly.Parse("2025-12-09"), DateOnly.Parse("2025-10-31"), 27005m, "url", null, DateOnly.Parse("2025-08-02")),
            new SecFactValue("us-gaap", "Revenues", "USD", 2026, "FY", "10-K", DateOnly.Parse("2026-03-16"), DateOnly.Parse("2026-01-30"), 113538m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-06-10"), DateOnly.Parse("2025-05-02"), 568m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 1243m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "Q3", "10-Q", DateOnly.Parse("2025-12-09"), DateOnly.Parse("2025-10-31"), 1912m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "PaymentsToAcquirePropertyPlantAndEquipment", "USD", 2026, "FY", "10-K", DateOnly.Parse("2026-03-16"), DateOnly.Parse("2026-01-30"), 2633m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q1", "10-Q", DateOnly.Parse("2025-06-10"), DateOnly.Parse("2025-05-02"), 2796m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q2", "10-Q", DateOnly.Parse("2025-09-08"), DateOnly.Parse("2025-08-01"), 5339m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "Q3", "10-Q", DateOnly.Parse("2025-12-09"), DateOnly.Parse("2025-10-31"), 6511m, "url", null, DateOnly.Parse("2025-02-01")),
            new SecFactValue("us-gaap", "NetCashProvidedByUsedInOperatingActivities", "USD", 2026, "FY", "10-K", DateOnly.Parse("2026-03-16"), DateOnly.Parse("2026-01-30"), 11185m, "url", null, DateOnly.Parse("2025-02-01"))
        };

        var metrics = SecMetricExtractor.Extract(facts).ToList();

        Assert.Contains(metrics, metric => metric.MetricName == "Revenue" && metric.FiscalQuarter == 4 && metric.Value == 33379m);
        Assert.Contains(metrics, metric => metric.MetricName == "Quarterly Capex" && metric.FiscalQuarter == 4 && metric.Value == 721m);
        Assert.Contains(metrics, metric => metric.MetricName == "Operating Cash Flow" && metric.FiscalQuarter == 4 && metric.Value == 4674m);
        Assert.Contains(metrics, metric => metric.MetricName == "Capex / OCF" && metric.FiscalQuarter == 4 && metric.Value == 15.43m);
        Assert.Contains(metrics, metric => metric.MetricName == "Capex QoQ Growth" && metric.FiscalQuarter == 4);
    }
}
