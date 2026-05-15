namespace AiCapex.Application.Scoring;

public static class DerivedFinancialSignalScorer
{
    public static decimal ScoreCapexOcf(decimal ratio) =>
        RoundDisplayCurve(10d * Math.Tanh((50d - (double)ratio) / 60d));

    public static decimal ScoreCapexGrowth(decimal growth) =>
        RoundDisplayCurve(10d * Math.Tanh((double)growth / 60d));

    private static decimal RoundDisplayCurve(double value) =>
        Math.Round((decimal)value, 1, MidpointRounding.AwayFromZero);
}
