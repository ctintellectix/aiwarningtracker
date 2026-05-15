using AiCapex.Application.Scoring;

namespace AiCapex.Application.Tests.RiskScoring;

public class SignalScoreInterpreterTests
{
    [Fact]
    public void Keeps_all_signal_scores_on_the_same_native_scale()
    {
        Assert.Equal(4m, SignalScoreInterpreter.ToScoringSignal(4m, "RSS AI narrative signal"));
        Assert.Equal(-10m, SignalScoreInterpreter.ToScoringSignal(-10m, "Transcript AI narrative signal"));
    }

    [Fact]
    public void Leaves_non_ai_scores_on_their_native_scale()
    {
        Assert.Equal(-10m, SignalScoreInterpreter.ToScoringSignal(-30m, "Manual analyst note"));
    }

    [Fact]
    public void Clamps_scores_to_unified_signal_range()
    {
        Assert.Equal(10m, SignalScoreInterpreter.ToScoringSignal(40m, "RSS AI narrative signal"));
    }

    [Theory]
    [InlineData(3.65, 3.7)]
    [InlineData(-25, -10)]
    [InlineData(60, 10)]
    public void Converts_internal_signal_to_trader_facing_display_scale(decimal internalScore, decimal expected)
    {
        Assert.Equal(expected, SignalScoreInterpreter.ToDisplaySignal(internalScore));
    }
}
