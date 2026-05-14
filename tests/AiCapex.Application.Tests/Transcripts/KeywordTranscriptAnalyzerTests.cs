using AiCapex.Application.Transcripts;

namespace AiCapex.Application.Tests.Transcripts;

public class KeywordTranscriptAnalyzerTests
{
    [Fact]
    public void Counts_keywords_by_signal_group_case_insensitively()
    {
        const string text = """
            AI infrastructure and GPU capacity remain supply constrained.
            HBM and advanced packaging capacity are improving, but power availability is still a grid constraint.
            We do not see a digestion period or utilization decline.
            """;

        var mentions = new KeywordTranscriptAnalyzer().Analyze(text);

        Assert.Equal(2, mentions.Single(x => x.Group == "AI infrastructure").Count);
        Assert.Equal(1, mentions.Single(x => x.Group == "Positive demand").Count);
        Assert.Equal(2, mentions.Single(x => x.Group == "Slowdown warning").Count);
        Assert.Equal(1, mentions.Single(x => x.Group == "Memory/HBM").Count);
        Assert.Equal(1, mentions.Single(x => x.Group == "Packaging").Count);
        Assert.Equal(2, mentions.Single(x => x.Group == "Power").Count);
    }

    [Fact]
    public void Produces_directional_signal_from_positive_and_warning_language()
    {
        const string text = "Demand exceeds supply and allocation remain strong, but optimize spend appeared once.";

        var signal = new KeywordTranscriptAnalyzer().ScoreDirectionalSignal(text);

        Assert.True(signal > 0);
    }
}
