using AiCapex.Domain.Scoring;

namespace AiCapex.Application.Scoring;

public sealed class RiskScoreCalculator(RiskScoreWeights weights)
{
    public RiskScoreResult Calculate(IEnumerable<RiskScoreInput> inputs)
    {
        var inputByCategory = inputs
            .GroupBy(x => x.Category)
            .ToDictionary(x => x.Key, x => x.Average(v => v.Signal));

        decimal totalWeight = 0;
        decimal weightedScore = 0;
        var contributions = new List<RiskScoreContribution>();

        foreach (RiskScoreCategory category in Enum.GetValues<RiskScoreCategory>())
        {
            var weight = weights.GetWeight(category);
            totalWeight += weight;
            var signal = Math.Clamp(inputByCategory.GetValueOrDefault(category, 0), -100, 100);

            // Signal values are +100 bullish to -100 bearish. Convert to a 0-100 expansion scale
            // where 100 means stronger capex expansion and 0 means higher slowdown/rollover risk.
            var categoryExpansion = (100 + signal) / 2;
            var weightedPoints = categoryExpansion * weight / 100;
            weightedScore += weightedPoints;
            contributions.Add(new RiskScoreContribution(category, signal, decimal.Round(weightedPoints, 2)));
        }

        var normalized = totalWeight == 0 ? 0 : weightedScore * 100 / totalWeight;
        var score = (int)Math.Round(Math.Clamp(normalized, 0, 100), MidpointRounding.AwayFromZero);
        return new RiskScoreResult(score, GetBand(score), contributions);
    }

    public static string GetBand(int score) => score switch
    {
        <= 24 => "Capex rollover risk",
        <= 39 => "Slowdown forming",
        <= 54 => "Watch zone",
        <= 74 => "Healthy expansion",
        _ => "Bullish acceleration"
    };
}
