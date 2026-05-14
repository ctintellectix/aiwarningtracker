using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure.Sec;

public sealed class SecClient : ISecClient
{
    private readonly HttpClient httpClient;
    private readonly SecOptions options;

    public SecClient(HttpClient httpClient, IOptions<SecOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(this.options.UserAgent))
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AiCapexSlowdownMonitor/1.0 (contact@example.com)");
        }
        this.httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<(string Json, bool UsedLiveData, string SourceUrl)> GetCompanyFactsAsync(string cik, string ticker, CancellationToken cancellationToken = default)
    {
        var paddedCik = cik.PadLeft(10, '0');
        var sourceUrl = $"https://data.sec.gov/api/xbrl/companyfacts/CIK{paddedCik}.json";
        var cachePath = GetCachePath($"{ticker.ToUpperInvariant()}-companyfacts.json");

        try
        {
            var json = await httpClient.GetStringAsync(sourceUrl, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, json, cancellationToken);
            return (json, true, sourceUrl);
        }
        catch when (File.Exists(cachePath))
        {
            return (await File.ReadAllTextAsync(cachePath, cancellationToken), false, sourceUrl);
        }
    }

    public async Task<string?> GetTickerCikMappingJsonAsync(CancellationToken cancellationToken = default)
    {
        const string sourceUrl = "https://www.sec.gov/files/company_tickers.json";
        var cachePath = GetCachePath("company_tickers.json");
        try
        {
            var json = await httpClient.GetStringAsync(sourceUrl, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, json, cancellationToken);
            return json;
        }
        catch when (File.Exists(cachePath))
        {
            return await File.ReadAllTextAsync(cachePath, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private string GetCachePath(string fileName) => Path.Combine(options.CacheDirectory, fileName);
}
