using System.Net;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.Extensions.Options;

namespace AiCapex.Application.Tests.Ingestion;

public class FmpTranscriptProviderTests
{
    [Fact]
    public async Task Returns_not_configured_when_api_key_is_missing()
    {
        var provider = new FmpTranscriptProvider(new HttpClient(new StaticHttpHandler("[]")), Options.Create(new FmpOptions()));

        var result = await provider.GetTranscriptAsync("MSFT", 2026, 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task Fetches_latest_transcript_from_dates_then_transcript_endpoint()
    {
        var handler = new RoutingHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("earning-call-transcript-dates"))
            {
                return """
                    [{"symbol":"MSFT","year":2026,"quarter":1,"date":"2026-01-30"},{"symbol":"MSFT","year":2025,"quarter":4,"date":"2025-10-30"}]
                    """;
            }

            Assert.Contains("symbol=MSFT", request.RequestUri.Query);
            Assert.Contains("year=2026", request.RequestUri.Query);
            Assert.Contains("quarter=1", request.RequestUri.Query);
            return """
                [{"symbol":"MSFT","year":2026,"quarter":1,"date":"2026-01-30","content":"AI infrastructure demand exceeds supply. HBM allocation remains tight."}]
                """;
        });
        var provider = new FmpTranscriptProvider(new HttpClient(handler), Options.Create(new FmpOptions { Enabled = true, ApiKey = "test-key" }));

        var dates = await provider.GetTranscriptDatesAsync("MSFT");
        var result = await provider.GetTranscriptAsync("MSFT", 2026, 1);

        Assert.NotEmpty(dates);
        Assert.NotNull(result);
        Assert.Equal("MSFT", result!.Ticker);
        Assert.Equal(2026, result.FiscalYear);
        Assert.Equal(1, result.FiscalQuarter);
        Assert.Contains("AI infrastructure", result.RawText);
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
