using AiCapex.Application.Scoring;
using AiCapex.Domain.Scoring;

namespace AiCapex.Application.Tests.RiskScoring;

public class RiskScoreCalculatorTests
{
    [Fact]
    public void Calculates_weighted_score_from_configured_category_signals()
    {
        var weights = RiskScoreWeights.Default;
        var inputs = new[]
        {
            new RiskScoreInput(RiskScoreCategory.HyperscalerCapexRevisionTrend, 40),
            new RiskScoreInput(RiskScoreCategory.HbmDramPricingAllocation, 20),
            new RiskScoreInput(RiskScoreCategory.CowosAdvancedPackaging, 0),
            new RiskScoreInput(RiskScoreCategory.DataCenterPower, -20),
            new RiskScoreInput(RiskScoreCategory.AiRevenueMonetization, 60),
            new RiskScoreInput(RiskScoreCategory.FinancialStressFreeCashFlow, 80)
        };

        var snapshot = new RiskScoreCalculator(weights).Calculate(inputs);

        Assert.Equal(64, snapshot.Score);
        Assert.Equal("Slowdown forming", snapshot.Band);
    }

    [Fact]
    public void Clamps_category_signals_before_scoring()
    {
        var inputs = new[]
        {
            new RiskScoreInput(RiskScoreCategory.HyperscalerCapexRevisionTrend, 250),
            new RiskScoreInput(RiskScoreCategory.HbmDramPricingAllocation, 250),
            new RiskScoreInput(RiskScoreCategory.CowosAdvancedPackaging, 250),
            new RiskScoreInput(RiskScoreCategory.DataCenterPower, 250),
            new RiskScoreInput(RiskScoreCategory.AiRevenueMonetization, 250),
            new RiskScoreInput(RiskScoreCategory.FinancialStressFreeCashFlow, 250)
        };

        var snapshot = new RiskScoreCalculator(RiskScoreWeights.Default).Calculate(inputs);

        Assert.Equal(100, snapshot.Score);
        Assert.Equal("Capex rollover risk", snapshot.Band);
    }

    [Theory]
    [InlineData(25, "Bullish acceleration")]
    [InlineData(45, "Healthy expansion")]
    [InlineData(60, "Watch zone")]
    [InlineData(75, "Slowdown forming")]
    [InlineData(76, "Capex rollover risk")]
    public void Assigns_risk_bands_from_score(int score, string expectedBand)
    {
        Assert.Equal(expectedBand, RiskScoreCalculator.GetBand(score));
    }
}
