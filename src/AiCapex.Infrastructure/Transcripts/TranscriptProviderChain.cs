using AiCapex.Application.Ingestion;

namespace AiCapex.Infrastructure.Transcripts;

public sealed class TranscriptProviderChain(IEnumerable<ITranscriptProvider> providers) : ITranscriptProviderChain
{
    public async Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default)
    {
        foreach (var provider in providers)
        {
            try
            {
                var result = await provider.GetTranscriptAsync(ticker, year, quarter, cancellationToken);
                if (result is not null)
                {
                    return result;
                }
            }
            catch
            {
                // Provider failures should never break the chain. Observability can be added with ILogger.
            }
        }

        return null;
    }
}
