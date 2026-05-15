using AiCapex.Application.Alerts;
using AiCapex.Application.Ingestion;
using AiCapex.Application.Scoring;
using AiCapex.Application.Services;
using AiCapex.Application.Transcripts;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/transcripts")]
public sealed class TranscriptsController(
    ITranscriptProviderChain providerChain,
    EarningsCallBizTranscriptProvider earningsCallBizProvider,
    ITranscriptStorageService transcriptStorage,
    IRiskScoringService scoringService,
    IAlertGenerationService alertGenerationService) : ControllerBase
{
    [HttpGet("{ticker}/{year:int}/{quarter:int}")]
    public async Task<IActionResult> GetTranscript(string ticker, int year, int quarter, CancellationToken cancellationToken)
    {
        var transcript = await providerChain.GetTranscriptAsync(ticker, year, quarter, cancellationToken);
        return transcript is null
            ? NotFound(new { message = "No transcript provider result found for this ticker, fiscal year, and quarter." })
            : Ok(transcript);
    }

    [HttpGet("earningscallbiz/{market}/{ticker}/{year:int}/{quarter:int}")]
    public async Task<IActionResult> GetEarningsCallBizTranscript(string market, string ticker, int year, int quarter, CancellationToken cancellationToken)
    {
        var transcript = await earningsCallBizProvider.GetTranscriptAsync(market, ticker, year, quarter, cancellationToken);
        if (transcript is null)
        {
            return NotFound(new { message = "No public EarningsCallBiz transcript found for this market, ticker, fiscal year, and quarter." });
        }

        await transcriptStorage.StoreAsync(transcript, cancellationToken);
        await scoringService.RecalculateAsync(cancellationToken);
        await alertGenerationService.GenerateAsync(cancellationToken);

        return Ok(new
        {
            ticker = transcript.Ticker,
            market = transcript.Market ?? market.Trim().ToLowerInvariant(),
            year = transcript.FiscalYear,
            quarter = transcript.FiscalQuarter,
            provider = transcript.Provider,
            sourceUrl = transcript.SourceUrl,
            callDate = transcript.CallDate,
            confidenceScore = transcript.ConfidenceScore,
            rawText = transcript.RawText
        });
    }
}
