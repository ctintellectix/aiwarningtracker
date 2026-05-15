using AiCapex.Application.Analysis;
using AiCapex.Infrastructure.Analysis;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiCapex.Application.Tests.Analysis;

public class DocumentNarrativeAnalysisServiceTests
{
    [Fact]
    public async Task Does_not_use_keyword_fallback_for_rss_when_openai_is_disabled()
    {
        var service = new DocumentNarrativeAnalysisService(
            new HttpClient(),
            Options.Create(new OpenAiOptions { Enabled = false }),
            NullLogger<DocumentNarrativeAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(new DocumentNarrativeAnalysisRequest(
            "RSS",
            "HBM allocation remains tight",
            "Demand exceeds supply for HBM and CoWoS capacity."));

        Assert.False(result.UsedFallback);
        Assert.Equal("Unavailable", result.Provider);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public async Task Does_not_use_keyword_fallback_for_transcripts_when_openai_is_disabled()
    {
        var service = new DocumentNarrativeAnalysisService(
            new HttpClient(),
            Options.Create(new OpenAiOptions { Enabled = false }),
            NullLogger<DocumentNarrativeAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(new DocumentNarrativeAnalysisRequest(
            "Transcript",
            "Micron call",
            "Demand exceeds supply for HBM and CoWoS capacity.",
            "MU"));

        Assert.False(result.UsedFallback);
        Assert.Equal("Unavailable", result.Provider);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public async Task Does_not_use_keyword_fallback_for_rss_when_http_client_is_misconfigured()
    {
        var service = new DocumentNarrativeAnalysisService(
            new HttpClient(),
            Options.Create(new OpenAiOptions { Enabled = true, ApiKey = "test-key" }),
            NullLogger<DocumentNarrativeAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(new DocumentNarrativeAnalysisRequest(
            "RSS",
            "HBM allocation remains tight",
            "Demand exceeds supply for HBM and CoWoS capacity."));

        Assert.False(result.UsedFallback);
        Assert.Equal("Unavailable", result.Provider);
    }

    [Fact]
    public async Task Prompt_calibrates_language_intensity_to_score_magnitude()
    {
        var handler = new RecordingHandler();
        var service = new DocumentNarrativeAnalysisService(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") },
            Options.Create(new OpenAiOptions { Enabled = true, ApiKey = "test-key", Model = "test-model" }),
            NullLogger<DocumentNarrativeAnalysisService>.Instance);

        await service.AnalyzeAsync(new DocumentNarrativeAnalysisRequest(
            "RSS",
            "HBM allocation remains tight",
            "Demand exceeds supply for HBM and CoWoS capacity."));

        Assert.Contains("Reserve emphatic language", handler.RequestBody);
        Assert.Contains("\"minimum\":-10", handler.RequestBody);
        Assert.Contains("\"maximum\":10", handler.RequestBody);
    }

    [Fact]
    public async Task Prompt_defines_retail_friendly_score_bands()
    {
        var handler = new RecordingHandler();
        var service = new DocumentNarrativeAnalysisService(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") },
            Options.Create(new OpenAiOptions { Enabled = true, ApiKey = "test-key", Model = "test-model" }),
            NullLogger<DocumentNarrativeAnalysisService>.Instance);

        await service.AnalyzeAsync(new DocumentNarrativeAnalysisRequest(
            "Transcript",
            "TSMC call",
            "AI-related demand was extremely robust and capex guidance moved higher.",
            "TSM"));

        Assert.Contains("0 means no supported signal", handler.RequestBody);
        Assert.Contains("barely directional", handler.RequestBody);
        Assert.Contains("a clear directional signal", handler.RequestBody);
        Assert.Contains("a strong signal", handler.RequestBody);
        Assert.Contains("an exceptional or unusually strong signal", handler.RequestBody);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "output": [
                        {
                          "content": [
                            {
                              "type": "output_text",
                              "text": "{\"summary\":\"ok\",\"confidence\":90,\"signals\":[]}"
                            }
                          ]
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
