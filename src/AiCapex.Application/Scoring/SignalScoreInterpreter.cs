namespace AiCapex.Application.Scoring;

public static class SignalScoreInterpreter
{
    public static decimal ToScoringSignal(decimal rawScore, string? signalName)
        => Math.Round(Math.Clamp(rawScore, -10m, 10m), 1, MidpointRounding.AwayFromZero);

    public static decimal ToDisplaySignal(decimal internalScore) =>
        Math.Round(Math.Clamp(internalScore, -10m, 10m), 1, MidpointRounding.AwayFromZero);

    public static decimal FromDisplaySignal(decimal displayScore) =>
        Math.Round(Math.Clamp(displayScore, -10m, 10m), 1, MidpointRounding.AwayFromZero);
}
