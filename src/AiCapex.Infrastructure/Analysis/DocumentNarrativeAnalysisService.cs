using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiCapex.Application.Analysis;
using AiCapex.Application.Transcripts;
using AiCapex.Domain.Scoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure.Analysis;

public sealed class DocumentNarrativeAnalysisService(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    ILogger<DocumentNarrativeAnalysisService> logger) : IDocumentNarrativeAnalysisService
{
    private readonly OpenAiOptions options = options.Value;

    public async Task<DocumentNarrativeAnalysisResult> AnalyzeAsync(DocumentNarrativeAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return BuildUnavailable();
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            message.Content = new StringContent(JsonSerializer.Serialize(BuildRequest(request)), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(message, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParseResponse(payload);
            return parsed with { RawJson = payload };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(
                ex,
                "OpenAI narrative analysis failed; no AI signals will be generated for {Title}.",
                request.Title);
            return BuildUnavailable();
        }
    }

    private object BuildRequest(DocumentNarrativeAnalysisRequest request) => new
    {
        model = options.Model,
        input = new object[]
        {
            new
            {
                role = "system",
                content = new[]
                {
                    new
                    {
                        type = "input_text",
                        text = """
You analyze AI infrastructure capex documents for a monitoring dashboard.
Return expansion-supportive scores where positive means stronger AI capex momentum and negative means slowdown risk.
Assess only categories supported by the source. Keep summaries concise, grammatical, and source-grounded.
Power constraints are bullish only when they clearly reflect strong demand; delays caused by weak demand are bearish.
Reserve emphatic language such as "exceptional", "surging", or "collapse" for large-magnitude scores; use measured wording for mild scores near zero.
Use the score range deliberately:
0 means no supported signal.
+/-1 means barely directional.
+/-2 to +/-4 means a clear directional signal.
+/-5 to +/-7 means a strong signal.
+/-8 to +/-10 means an exceptional or unusually strong signal.
Do not assign near-zero scores to clearly strong evidence such as raised capex guidance, sold-out capacity, unusually tight allocation, severe pricing pressure, or explicit capex cuts.
"""
                    }
                }
            },
            new
            {
                role = "user",
                content = new[]
                {
                    new
                    {
                        type = "input_text",
                        text = $"""
Document type: {request.DocumentType}
Ticker: {request.Ticker ?? "N/A"}
Title: {request.Title}

Document:
{TrimForModel(request.Text)}
"""
                    }
                }
            }
        },
        text = new
        {
            format = new
            {
                type = "json_schema",
                name = "document_narrative_analysis",
                strict = true,
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new
                    {
                        summary = new { type = "string" },
                        confidence = new { type = "integer", minimum = 0, maximum = 100 },
                        signals = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                additionalProperties = false,
                                properties = new
                                {
                                    category = new
                                    {
                                        type = "string",
                                        @enum = Enum.GetNames<RiskScoreCategory>()
                                    },
                                    scoreImpact = new { type = "number", minimum = -10, maximum = 10 },
                                    summary = new { type = "string" },
                                    explanation = new { type = "string" },
                                    confidence = new { type = "integer", minimum = 0, maximum = 100 }
                                },
                                required = new[] { "category", "scoreImpact", "summary", "explanation", "confidence" }
                            }
                        }
                    },
                    required = new[] { "summary", "confidence", "signals" }
                }
            }
        }
    };

    private DocumentNarrativeAnalysisResult ParseResponse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var outputText = document.RootElement
            .GetProperty("output")
            .EnumerateArray()
            .SelectMany(x => x.GetProperty("content").EnumerateArray())
            .First(x => x.GetProperty("type").GetString() == "output_text")
            .GetProperty("text")
            .GetString() ?? "{}";

        using var resultDocument = JsonDocument.Parse(outputText);
        var root = resultDocument.RootElement;
        var signals = root.GetProperty("signals")
            .EnumerateArray()
            .Select(signal => new DocumentCategorySignal(
                Enum.Parse<RiskScoreCategory>(signal.GetProperty("category").GetString()!, true),
                signal.GetProperty("scoreImpact").GetDecimal(),
                signal.GetProperty("summary").GetString() ?? "",
                signal.GetProperty("explanation").GetString() ?? "",
                signal.GetProperty("confidence").GetInt32()))
            .ToList();

        return new DocumentNarrativeAnalysisResult(
            "OpenAI",
            options.Model,
            false,
            root.GetProperty("summary").GetString() ?? "",
            root.GetProperty("confidence").GetInt32(),
            signals);
    }

    private static DocumentNarrativeAnalysisResult BuildFallback(DocumentNarrativeAnalysisRequest request)
    {
        var analyzer = new KeywordTranscriptAnalyzer();
        var mentions = analyzer.Analyze(request.Text);
        var impact = analyzer.ScoreDirectionalSignal(request.Text);
        var signals = mentions
            .GroupBy(x => MapGroup(x.Group))
            .Select(group => new DocumentCategorySignal(
                group.Key,
                impact,
                $"Keyword evidence found for {FormatCategory(group.Key)}.",
                $"Keyword groups: {string.Join(", ", group.Select(x => x.Group).Distinct())}",
                45))
            .ToList();

        return new DocumentNarrativeAnalysisResult(
            "KeywordFallback",
            null,
            true,
            signals.Count == 0 ? "No directional narrative signal detected." : "Keyword-based fallback analysis was used.",
            signals.Count == 0 ? 0 : 45,
            signals);
    }

    private static DocumentNarrativeAnalysisResult BuildUnavailable() =>
        new(
            "Unavailable",
            null,
            false,
            "OpenAI transcript analysis is unavailable.",
            0,
            []);

    private static RiskScoreCategory MapGroup(string group) => group switch
    {
        "Memory/HBM" => RiskScoreCategory.HbmDramPricingAllocation,
        "Packaging" => RiskScoreCategory.CowosAdvancedPackaging,
        "Power" => RiskScoreCategory.DataCenterPower,
        "Capex" => RiskScoreCategory.HyperscalerCapexRevisionTrend,
        "Financial stress" => RiskScoreCategory.FinancialStressFreeCashFlow,
        _ => RiskScoreCategory.AiRevenueMonetization
    };

    private static string FormatCategory(RiskScoreCategory category) => category switch
    {
        RiskScoreCategory.HyperscalerCapexRevisionTrend => "hyperscaler capex",
        RiskScoreCategory.HbmDramPricingAllocation => "HBM/DRAM",
        RiskScoreCategory.CowosAdvancedPackaging => "CoWoS/packaging",
        RiskScoreCategory.DataCenterPower => "data center/power",
        RiskScoreCategory.AiRevenueMonetization => "AI revenue",
        RiskScoreCategory.FinancialStressFreeCashFlow => "financial stress/FCF",
        _ => category.ToString()
    };

    private static string TrimForModel(string text) => text.Length <= 60000 ? text : text[..60000];
}
