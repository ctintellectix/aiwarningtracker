using System.Text.Json;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Sec;

public sealed class SecTickerCikMapper(AiCapexDbContext db, ISecClient secClient) : ISecTickerCikMapper
{
    public async Task<string?> GetCikAsync(string ticker, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        var configured = await db.Companies.Where(x => x.Ticker == ticker).Select(x => x.Cik).SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var mappingJson = await secClient.GetTickerCikMappingJsonAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(mappingJson);
        foreach (var item in document.RootElement.EnumerateObject())
        {
            var row = item.Value;
            if (row.TryGetProperty("ticker", out var tickerValue) &&
                string.Equals(tickerValue.GetString(), ticker, StringComparison.OrdinalIgnoreCase) &&
                row.TryGetProperty("cik_str", out var cikValue))
            {
                var cik = cikValue.GetInt32().ToString("D10");
                var company = await db.Companies.SingleOrDefaultAsync(x => x.Ticker == ticker, cancellationToken);
                if (company is not null)
                {
                    company.Cik = cik;
                    await db.SaveChangesAsync(cancellationToken);
                }

                return cik;
            }
        }

        return null;
    }
}
