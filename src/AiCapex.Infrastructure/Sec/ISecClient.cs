namespace AiCapex.Infrastructure.Sec;

public interface ISecClient
{
    Task<(string Json, bool UsedLiveData, string SourceUrl)> GetCompanyFactsAsync(string cik, string ticker, CancellationToken cancellationToken = default);
    Task<string?> GetTickerCikMappingJsonAsync(CancellationToken cancellationToken = default);
}
