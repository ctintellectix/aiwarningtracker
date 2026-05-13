namespace AiCapex.Domain.Scoring;

public sealed record RiskScoreWeights(
    decimal HyperscalerCapexRevisionTrend,
    decimal HbmDramPricingAllocation,
    decimal CowosAdvancedPackaging,
    decimal DataCenterPower,
    decimal AiRevenueMonetization,
    decimal FinancialStressFreeCashFlow)
{
    public static RiskScoreWeights Default { get; } = new(30, 20, 15, 15, 10, 10);

    public decimal GetWeight(RiskScoreCategory category) => category switch
    {
        RiskScoreCategory.HyperscalerCapexRevisionTrend => HyperscalerCapexRevisionTrend,
        RiskScoreCategory.HbmDramPricingAllocation => HbmDramPricingAllocation,
        RiskScoreCategory.CowosAdvancedPackaging => CowosAdvancedPackaging,
        RiskScoreCategory.DataCenterPower => DataCenterPower,
        RiskScoreCategory.AiRevenueMonetization => AiRevenueMonetization,
        RiskScoreCategory.FinancialStressFreeCashFlow => FinancialStressFreeCashFlow,
        _ => 0
    };
}
