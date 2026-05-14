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
        Assert.Equal("Healthy expansion", snapshot.Band);
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
        Assert.Equal("Bullish acceleration", snapshot.Band);
    }

    [Theory]
    [InlineData(0, "Capex rollover risk")]
    [InlineData(25, "Slowdown forming")]
    [InlineData(40, "Watch zone")]
    [InlineData(55, "Healthy expansion")]
    [InlineData(75, "Bullish acceleration")]
    [InlineData(100, "Bullish acceleration")]
    public void Assigns_expansion_bands_from_score(int score, string expectedBand)
    {
        Assert.Equal(expectedBand, RiskScoreCalculator.GetBand(score));
    }
}
