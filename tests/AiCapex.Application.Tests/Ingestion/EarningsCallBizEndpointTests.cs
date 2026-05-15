using System.Net;
using AiCapex.Application.Alerts;
using AiCapex.Api.Controllers;
using AiCapex.Application.Dashboard;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Scoring;
using AiCapex.Application.Services;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiCapex.Application.Tests.Ingestion;

public class EarningsCallBizEndpointTests
{
    [Fact]
    public async Task Endpoint_returns_normalized_transcript_result()
    {
        var provider = new EarningsCallBizTranscriptProvider(
            new HttpClient(new StaticHttpHandler(SampleTranscriptHtml())) { BaseAddress = new Uri("https://earningscall.biz") },
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new EarningsCallBizOptions { Enabled = true, RequestDelayMs = 0 }),
            NullLogger<EarningsCallBizTranscriptProvider>.Instance);
        var controller = new TranscriptsController(new EmptyProviderChain(), provider, new RecordingStorageService(), new EmptyScoringService(), new EmptyAlertGenerationService());

        var response = await controller.GetEarningsCallBizTranscript("NASDAQ", "hood", 2026, 1, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var value = ok.Value!;
        Assert.Equal("HOOD", ReadProperty<string>(value, "ticker"));
        Assert.Equal("nasdaq", ReadProperty<string>(value, "market"));
        Assert.Equal(2026, ReadProperty<int>(value, "year"));
        Assert.Equal(1, ReadProperty<int>(value, "quarter"));
        Assert.Equal("EarningsCallBiz", ReadProperty<string>(value, "provider"));
        Assert.Contains("/e/nasdaq/s/hood/y/2026/q/q1", ReadProperty<string>(value, "sourceUrl"));
        Assert.True(ReadProperty<int>(value, "confidenceScore") >= 80);
        Assert.Contains("AI infrastructure", ReadProperty<string>(value, "rawText"));
    }

    private static T ReadProperty<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static string SampleTranscriptHtml()
    {
        var repeatedText = string.Join(Environment.NewLine, Enumerable.Repeat("""
            Operator
            Welcome to the quarterly earnings conference call.
            CEO
            AI infrastructure and inference demand remained healthy this quarter.
            CFO
            Capital expenditures and data center investment supported growth.
            Question-and-Answer Session
            Question
            How should investors think about backlog?
            Answer
            Backlog remains constructive.
            """, 35));

        return $$"""
            <html><body><main><article>
            <h1>HOOD Q1 2026 Earnings Call Transcript</h1>
            <time datetime="2026-04-28">April 28, 2026</time>
            <section>{{repeatedText}}</section>
            </article></main></body></html>
            """;
    }

    private sealed class StaticHttpHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private sealed class EmptyProviderChain : ITranscriptProviderChain
    {
        public Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default) =>
            Task.FromResult<TranscriptResult?>(null);
    }

    private sealed class RecordingStorageService : ITranscriptStorageService
    {
        public Task<ImportResultDto> StoreAsync(TranscriptResult transcript, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportResultDto(transcript.Provider, true, 1, 1, "stored"));
    }

    private sealed class EmptyScoringService : IRiskScoringService
    {
        public Task<RiskScoreRunResultDto> RecalculateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RiskScoreRunResultDto(50, 0, "Watch zone", "ok"));
    }

    private sealed class EmptyAlertGenerationService : IAlertGenerationService
    {
        public Task<ImportResultDto> GenerateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportResultDto("Watchlist alerts", true, 0, 0, "ok"));
    }

    private sealed class EmptyReadService : IAiCapexReadService
    {
        public Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MetricDto>> GetCompanyMetricsAsync(string ticker, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CompanyFinancialsDto?> GetCompanyFinancialsAsync(string ticker, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CategoryStatusDto>> GetIndicatorTrendsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<QuarterScoreDto>> GetRiskScoreHistoryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
