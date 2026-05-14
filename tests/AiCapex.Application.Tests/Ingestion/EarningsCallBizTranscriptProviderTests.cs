using System.Net;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiCapex.Application.Tests.Ingestion;

public class EarningsCallBizTranscriptProviderTests
{
    [Fact]
    public void Builds_normalized_url()
    {
        var url = EarningsCallBizTranscriptProvider.BuildTranscriptUrl("NASDAQ", "NVDA", 2026, 1, "https://earningscall.biz/");

        Assert.Equal("https://earningscall.biz/e/nasdaq/s/nvda/y/2026/q/q1", url);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Invalid_quarter_throws_validation_error(int quarter)
    {
        var provider = CreateProvider(new StaticHttpHandler(HttpStatusCode.OK, ""));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.GetTranscriptAsync("nasdaq", "NVDA", 2026, quarter));
    }

    [Fact]
    public async Task Not_found_returns_null_and_caches_negative_result()
    {
        var handler = new CountingHttpHandler(HttpStatusCode.NotFound, "");
        var provider = CreateProvider(handler);

        var first = await provider.GetTranscriptAsync("nasdaq", "NVDA", 2026, 1);
        var second = await provider.GetTranscriptAsync("nasdaq", "NVDA", 2026, 1);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Rate_limit_returns_cached_transcript_when_available()
    {
        var handler = new QueueHttpHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleTranscriptHtml("HOOD")) },
            new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("rate limited") });
        var provider = CreateProvider(handler, cacheDays: 0);

        var first = await provider.GetTranscriptAsync("nasdaq", "HOOD", 2026, 1);
        var second = await provider.GetTranscriptAsync("nasdaq", "HOOD", 2026, 1);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.RawText, second!.RawText);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Html_parser_extracts_clean_transcript_text()
    {
        var provider = CreateProvider(new StaticHttpHandler(HttpStatusCode.OK, SampleTranscriptHtml("HOOD")));

        var result = await provider.GetTranscriptAsync("NASDAQ", "HOOD", 2026, 1);

        Assert.NotNull(result);
        Assert.Equal("HOOD", result!.Ticker);
        Assert.Equal("EarningsCallBiz", result.Provider);
        Assert.Equal(2026, result.FiscalYear);
        Assert.Equal(1, result.FiscalQuarter);
        Assert.Contains("Operator", result.RawText);
        Assert.Contains("Question-and-Answer Session", result.RawText);
        Assert.DoesNotContain("Download our app", result.RawText);
        Assert.DoesNotContain("function tracker", result.RawText);
        Assert.True(result.ConfidenceScore >= 80);
    }

    [Fact]
    public async Task Short_generic_page_returns_null()
    {
        var provider = CreateProvider(new StaticHttpHandler(HttpStatusCode.OK, "<html><body><nav>Home</nav><main>Welcome to our site.</main></body></html>"));

        var result = await provider.GetTranscriptAsync("nasdaq", "HOOD", 2026, 1);

        Assert.Null(result);
    }

    private static EarningsCallBizTranscriptProvider CreateProvider(HttpMessageHandler handler, int cacheDays = 7)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://earningscall.biz"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        return new EarningsCallBizTranscriptProvider(
            httpClient,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new EarningsCallBizOptions
            {
                Enabled = true,
                BaseUrl = "https://earningscall.biz",
                CacheDays = cacheDays,
                RequestDelayMs = 0
            }),
            NullLogger<EarningsCallBizTranscriptProvider>.Instance);
    }

    private static string SampleTranscriptHtml(string ticker)
    {
        var repeatedText = string.Join(Environment.NewLine, Enumerable.Repeat("""
            Operator
            Good afternoon, and welcome to the quarterly earnings conference call.

            Chief Executive Officer
            We delivered another strong quarter as customers adopted AI infrastructure and inference products.

            Chief Financial Officer
            Capital expenditures and data center investment remained disciplined while revenue growth continued.

            Question-and-Answer Session
            Question
            Can you discuss demand for the quarter?

            Answer
            Demand remains healthy, and our backlog supports future growth.
            """, 35));

        return $$"""
            <html>
              <head>
                <title>{{ticker}} Q1 2026 Earnings Call Transcript</title>
                <script>function tracker(){ return false; }</script>
                <style>.hidden { display:none; }</style>
              </head>
              <body>
                <nav>Home Markets Transcripts Download our app</nav>
                <main>
                  <article>
                    <h1>{{ticker}} Q1 2026 Earnings Call Transcript</h1>
                    <time datetime="2026-04-28">April 28, 2026</time>
                    <p>Share buttons</p>
                    <section>{{repeatedText}}</section>
                  </article>
                </main>
                <footer>About Contact Privacy</footer>
              </body>
            </html>
            """;
    }

    private sealed class StaticHttpHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private sealed class CountingHttpHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }

    private sealed class QueueHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(responses.Dequeue());
        }
    }
}
