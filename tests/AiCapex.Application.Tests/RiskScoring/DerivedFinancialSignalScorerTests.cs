using AiCapex.Application.Scoring;

namespace AiCapex.Application.Tests.RiskScoring;

public class DerivedFinancialSignalScorerTests
{
    [Fact]
    public void Scores_capex_ocf_with_shared_display_curve() =>
        Assert.Equal(-4.6m, DerivedFinancialSignalScorer.ScoreCapexOcf(80));

    [Fact]
    public void Scores_capex_growth_with_shared_display_curve() =>
        Assert.Equal(3.9m, DerivedFinancialSignalScorer.ScoreCapexGrowth(25));
}
