using AiCapex.Application.Ingestion;
using AiCapex.Infrastructure.Transcripts;

namespace AiCapex.Application.Tests.Ingestion;

public class TranscriptProviderChainTests
{
    [Fact]
    public async Task Uses_providers_in_priority_order_and_stops_after_first_result()
    {
        var first = new RecordingProvider("Cached", null);
        var secondResult = new TranscriptResult("NVDA", 2026, 1, new DateOnly(2026, 5, 1), "EarningsCallBiz", "NVDA transcript", "AI infrastructure demand exceeds supply.", "https://earningscall.biz/source", 95);
        var second = new RecordingProvider("EarningsCallBiz", secondResult);
        var third = new RecordingProvider("Unused", null, throwIfCalled: true);
        var chain = new TranscriptProviderChain([first, second, third]);

        var result = await chain.GetTranscriptAsync("NVDA", 2026, 1);

        Assert.NotNull(result);
        Assert.Equal("EarningsCallBiz", result!.Provider);
        Assert.True(first.WasCalled);
        Assert.True(second.WasCalled);
        Assert.False(third.WasCalled);
    }

    [Fact]
    public async Task Returns_null_when_no_provider_finds_transcript()
    {
        var chain = new TranscriptProviderChain([
            new RecordingProvider("Cached", null),
            new RecordingProvider("EarningsCallBiz", null)
        ]);

        var result = await chain.GetTranscriptAsync("MSFT", 2026, 1);

        Assert.Null(result);
    }

    private sealed class RecordingProvider(string name, TranscriptResult? result, bool throwIfCalled = false) : ITranscriptProvider
    {
        public bool WasCalled { get; private set; }

        public Task<List<TranscriptMetadata>> GetTranscriptDatesAsync(string ticker, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TranscriptMetadata>());

        public Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            if (throwIfCalled)
            {
                throw new InvalidOperationException($"{name} should not have been called.");
            }

            return Task.FromResult(result);
        }
    }
}
