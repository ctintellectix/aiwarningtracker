using System.Text.RegularExpressions;

namespace AiCapex.Application.Transcripts;

public sealed class KeywordTranscriptAnalyzer
{
    private static readonly IReadOnlyDictionary<string, string[]> KeywordGroups = new Dictionary<string, string[]>
    {
        ["AI infrastructure"] = ["AI infrastructure", "accelerated computing", "training cluster", "inference", "GPU capacity", "compute capacity"],
        ["Capex"] = ["capital expenditures", "capex", "data center investment", "infrastructure spend", "property and equipment"],
        ["Positive demand"] = ["supply constrained", "capacity constrained", "demand exceeds supply", "sold out", "allocation", "prepayment", "long-term agreement"],
        ["Slowdown warning"] = ["digestion period", "optimize spend", "reduce capex", "moderate growth", "lower demand", "inventory correction", "pricing pressure", "utilization decline"],
        ["Memory/HBM"] = ["HBM", "high bandwidth memory", "DRAM", "memory bandwidth", "advanced memory"],
        ["Packaging"] = ["CoWoS", "advanced packaging", "2.5D packaging", "interposer", "packaging capacity"],
        ["Power"] = ["power availability", "grid constraint", "interconnect", "substation", "energy constraint", "cooling constraint"]
    };

    public IReadOnlyList<TranscriptMentionResult> Analyze(string text)
    {
        return KeywordGroups
            .Select(group =>
            {
                var count = CountGroup(text, group.Value);
                return new TranscriptMentionResult(group.Key, string.Join(", ", group.Value), count);
            })
            .Where(result => result.Count > 0)
            .ToList();
    }

    public decimal ScoreDirectionalSignal(string text)
    {
        var positive = KeywordGroups["Positive demand"].Sum(keyword => Count(text, keyword));
        var warnings = KeywordGroups["Slowdown warning"].Sum(keyword => Count(text, keyword));
        return Math.Clamp((warnings - positive) * 20, -100, 100);
    }

    private static int CountGroup(string text, IEnumerable<string> keywords)
    {
        var occupied = new List<(int Start, int End)>();
        var count = 0;

        foreach (var keyword in keywords.OrderByDescending(x => x.Length))
        {
            var pattern = $@"(?<!\w){Regex.Escape(keyword)}(?!\w)";
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var start = match.Index;
                var end = match.Index + match.Length;
                if (occupied.Any(range => start < range.End && end > range.Start))
                {
                    continue;
                }

                occupied.Add((start, end));
                count++;
            }
        }

        return count;
    }

    private static int Count(string text, string keyword)
    {
        var pattern = $@"(?<!\w){Regex.Escape(keyword)}(?!\w)";
        return Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
    }
}
