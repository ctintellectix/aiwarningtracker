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
            new RiskScoreInput(RiskScoreCategory.HyperscalerCapexRevisionTrend, 4),
            new RiskScoreInput(RiskScoreCategory.HbmDramPricingAllocation, 2),
            new RiskScoreInput(RiskScoreCategory.CowosAdvancedPackaging, 0),
            new RiskScoreInput(RiskScoreCategory.DataCenterPower, -2),
            new RiskScoreInput(RiskScoreCategory.AiRevenueMonetization, 6),
            new RiskScoreInput(RiskScoreCategory.FinancialStressFreeCashFlow, 8)
        };

        var snapshot = new RiskScoreCalculator(weights).Calculate(inputs);

        Assert.Equal(64, snapshot.Score);
        Assert.Equal("Strong", snapshot.Band);
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
        Assert.Equal("Very strong", snapshot.Band);
    }

    [Theory]
    [InlineData(0, "Very weak")]
    [InlineData(25, "Weak")]
    [InlineData(40, "Neutral")]
    [InlineData(60, "Strong")]
    [InlineData(80, "Very strong")]
    [InlineData(100, "Very strong")]
    public void Assigns_expansion_bands_from_score(int score, string expectedBand)
    {
        Assert.Equal(expectedBand, RiskScoreCalculator.GetBand(score));
    }
}
