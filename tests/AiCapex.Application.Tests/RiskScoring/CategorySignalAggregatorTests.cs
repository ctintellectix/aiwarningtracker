using AiCapex.Application.Scoring;

namespace AiCapex.Application.Tests.RiskScoring;

public class CategorySignalAggregatorTests
{
    [Fact]
    public void Lets_strong_bullish_evidence_lift_a_category_above_a_flat_average()
    {
        var score = CategorySignalAggregator.Aggregate([
            (15m, 90),
            (11m, 80),
            (2m, 70),
            (0m, 70)
        ]);

        Assert.True(score > 8m);
    }

    [Fact]
    public void Keeps_strong_bearish_evidence_visible()
    {
        var score = CategorySignalAggregator.Aggregate([
            (-10m, 90),
            (-6m, 70),
            (5m, 60)
        ]);

        Assert.True(score < -4m);
    }

    [Fact]
    public void Returns_zero_when_no_signals_exist()
    {
        Assert.Equal(0m, CategorySignalAggregator.Aggregate([]));
    }
}
