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

            // Signal values are -100 bullish to +100 bearish. Convert to a 0-100 risk scale,
            // then apply the configured category weight.
            var categoryRisk = (signal + 100) / 2;
            var weightedPoints = categoryRisk * weight / 100;
            weightedScore += weightedPoints;
            contributions.Add(new RiskScoreContribution(category, signal, decimal.Round(weightedPoints, 2)));
        }

        var normalized = totalWeight == 0 ? 0 : weightedScore * 100 / totalWeight;
        var score = (int)Math.Round(Math.Clamp(normalized, 0, 100), MidpointRounding.AwayFromZero);
        return new RiskScoreResult(score, GetBand(score), contributions);
    }

    public static string GetBand(int score) => score switch
    {
        <= 25 => "Bullish acceleration",
        <= 45 => "Healthy expansion",
        <= 60 => "Watch zone",
        <= 75 => "Slowdown forming",
        _ => "Capex rollover risk"
    };
}
