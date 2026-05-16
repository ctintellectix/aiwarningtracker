using System.Text.Json;

namespace AiCapex.Infrastructure.Sec;

public static class SecCompanyFactsParser
{
    public static readonly IReadOnlySet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PaymentsToAcquirePropertyPlantAndEquipment",
        "PaymentsToAcquireProductiveAssets",
        "CapitalExpendituresIncurredButNotYetPaid",
        "PropertyPlantAndEquipmentNet",
        "NetCashProvidedByUsedInOperatingActivities",
        "Revenues",
        "SalesRevenueNet",
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "LongTermDebt",
        "LongTermDebtCurrent",
        "ShortTermBorrowings",
        "DebtLongtermAndShorttermCombinedAmount",
        "DebtAndCapitalLeaseObligations",
        "DebtCurrent",
        "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities",
        "CashFlowsFromUsedInOperations",
        "Revenue",
        "Borrowings",
        "CurrentBorrowings",
        "NoncurrentBorrowings"
    };

    public static IReadOnlyList<SecFactValue> Parse(string json, string sourceUrl)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("facts", out var facts))
        {
            return [];
        }

        var values = new List<SecFactValue>();
        foreach (var taxonomyProperty in facts.EnumerateObject()
            .Where(x => x.Name.Equals("us-gaap", StringComparison.OrdinalIgnoreCase) ||
                        x.Name.Equals("ifrs-full", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var tagProperty in taxonomyProperty.Value.EnumerateObject())
            {
                if (!Tags.Contains(tagProperty.Name) ||
                    !tagProperty.Value.TryGetProperty("units", out var units))
                {
                    continue;
                }

                foreach (var unitProperty in units.EnumerateObject())
                {
                    if (unitProperty.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var fact in unitProperty.Value.EnumerateArray())
                    {
                        if (!fact.TryGetProperty("val", out var val) ||
                            !val.TryGetDecimal(out var decimalValue) ||
                            !fact.TryGetProperty("fy", out var fy) ||
                            fy.ValueKind != JsonValueKind.Number ||
                            !fy.TryGetInt32(out var fiscalYear))
                        {
                            continue;
                        }

                        var fiscalPeriod = GetString(fact, "fp");
                        var form = GetString(fact, "form");
                        if (string.IsNullOrWhiteSpace(fiscalPeriod) || string.IsNullOrWhiteSpace(form))
                        {
                            continue;
                        }

                        values.Add(new SecFactValue(
                            taxonomyProperty.Name,
                            tagProperty.Name,
                            unitProperty.Name,
                            fiscalYear,
                            fiscalPeriod,
                            form,
                            GetDate(fact, "filed"),
                            GetDate(fact, "end"),
                            decimalValue,
                            sourceUrl,
                            GetString(fact, "accn"),
                            GetDate(fact, "start")));
                    }
                }
            }
        }

        return values;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateOnly? GetDate(JsonElement element, string property)
    {
        var value = GetString(element, property);
        return DateOnly.TryParse(value, out var date) ? date : null;
    }
}
