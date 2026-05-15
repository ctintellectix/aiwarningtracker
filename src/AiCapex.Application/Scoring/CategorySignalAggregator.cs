namespace AiCapex.Application.Scoring;

public static class CategorySignalAggregator
{
    public static decimal Aggregate(IEnumerable<(decimal Score, int Confidence)> signals)
    {
        var values = signals.ToList();
        if (values.Count == 0)
        {
            return 0;
        }

        var weightedAverage = WeightedAverage(values);
        var strongestBullish = values.Max(x => x.Score);
        var strongestBearish = values.Min(x => x.Score);

        // Traders care about the direction of the body of evidence, but they also
        // care when one strong, credible signal appears before the full group average turns.
        // Give the stronger tail more influence on the side the evidence already leans toward.
        var aggregate = weightedAverage >= 0
            ? (weightedAverage * 0.60m) + (strongestBullish * 0.25m) + (strongestBearish * 0.15m)
            : (weightedAverage * 0.60m) + (strongestBullish * 0.15m) + (strongestBearish * 0.25m);
        return Math.Round(Math.Clamp(aggregate, -10m, 10m), 1);
    }

    private static decimal WeightedAverage(IReadOnlyList<(decimal Score, int Confidence)> values)
    {
        var weights = values.Select(x => Math.Max(x.Confidence, 1)).ToList();
        var totalWeight = weights.Sum();
        return values.Select((value, index) => value.Score * weights[index]).Sum() / totalWeight;
    }
}
