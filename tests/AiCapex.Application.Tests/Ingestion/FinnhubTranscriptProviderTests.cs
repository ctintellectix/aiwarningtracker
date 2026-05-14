using System.Net;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.Extensions.Options;

namespace AiCapex.Application.Tests.Ingestion;

public class FinnhubTranscriptProviderTests
{
    [Fact]
    public async Task Returns_null_when_disabled_or_api_key_is_missing()
    {
        var provider = new FinnhubTranscriptProvider(new HttpClient(new StaticHttpHandler("[]")), Options.Create(new FinnhubOptions()));

        var result = await provider.GetTranscriptAsync("MSFT", 2026, 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task Fetches_matching_transcript_from_list_then_transcript_endpoint()
    {
        var handler = new RoutingHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/stock/transcripts/list"))
            {
                Assert.Contains("symbol=MSFT", request.RequestUri.Query);
                Assert.Contains("token=test-key", request.RequestUri.Query);
                return """
                    {"symbol":"MSFT","transcripts":[{"id":"msft-q1-2026","title":"MSFT Q1 2026 Earnings Call","year":2026,"quarter":1,"time":"2026-01-30"}]}
                    """;
            }

            Assert.Contains("id=msft-q1-2026", request.RequestUri!.Query);
            return """
                {"symbol":"MSFT","year":2026,"quarter":1,"title":"MSFT Q1 2026 Earnings Call","time":"2026-01-30","transcript":[{"name":"CEO","speech":"AI infrastructure demand exceeds supply."},{"name":"CFO","speech":"HBM allocation remains tight."}]}
                """;
        });
        var provider = new FinnhubTranscriptProvider(new HttpClient(handler), Options.Create(new FinnhubOptions { Enabled = true, ApiKey = "test-key" }));

        var dates = await provider.GetTranscriptDatesAsync("MSFT");
        var result = await provider.GetTranscriptAsync("MSFT", 2026, 1);

        Assert.Single(dates);
        Assert.NotNull(result);
        Assert.Equal("Finnhub", result!.Provider);
        Assert.Equal("MSFT", result.Ticker);
        Assert.Equal(2026, result.FiscalYear);
        Assert.Equal(1, result.FiscalQuarter);
        Assert.Contains("CEO: AI infrastructure", result.RawText);
        Assert.Contains("CFO: HBM allocation", result.RawText);
    }

    [Fact]
    public void Parses_array_or_object_list_payloads()
    {
        const string arrayJson = """
            [{"id":"a","year":"2026","quarter":"1","title":"Array payload","time":"2026-01-30"}]
            """;
        const string objectJson = """
            {"transcripts":[{"id":"b","year":2026,"quarter":2,"title":"Object payload","time":"2026-04-30"}]}
            """;

        var arrayItem = Assert.Single(FinnhubTranscriptProvider.ParseTranscriptList(arrayJson, "MSFT", "https://finnhub.io/api/v1"));
        var objectItem = Assert.Single(FinnhubTranscriptProvider.ParseTranscriptList(objectJson, "MSFT", "https://finnhub.io/api/v1"));

        Assert.Equal("a", arrayItem.Id);
        Assert.Equal(1, arrayItem.Quarter);
        Assert.Equal("b", objectItem.Id);
        Assert.Equal(2, objectItem.Quarter);
    }

    private sealed class StaticHttpHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private sealed class RoutingHttpHandler(Func<HttpRequestMessage, string> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(route(request)) });
    }
}
