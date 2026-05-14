using AiCapex.Application.Dashboard;
using AiCapex.Application.Services;
using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Sec;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure.Services;

public sealed class DataSourceStatusService(AiCapexDbContext db, IOptions<SecOptions> secOptions, IConfiguration configuration) : IDataSourceStatusService
{
    public async Task<IReadOnlyList<DataSourceStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var secLastImport = await db.SourceDocuments
            .Where(x => x.Provider == "SEC EDGAR")
            .Select(x => x.RetrievedAtUtc)
            .ToListAsync(cancellationToken);
        var fmpLastImport = await db.SourceDocuments
            .Where(x => x.Provider == "FMP")
            .Select(x => x.RetrievedAtUtc)
            .ToListAsync(cancellationToken);
        var earningsCallBizLastImport = await db.SourceDocuments
            .Where(x => x.Provider == "EarningsCallBiz")
            .Select(x => x.RetrievedAtUtc)
            .ToListAsync(cancellationToken);
        var rssLastImport = await db.SourceDocuments
            .Where(x => x.SourceType == SourceType.NewsRss)
            .Select(x => x.RetrievedAtUtc)
            .ToListAsync(cancellationToken);

        var fmpKey = configuration["FMP_API_KEY"];
        var finnhubKey = configuration["FINNHUB_API_KEY"];
        var feedCount = configuration.GetSection("NewsFeeds").GetChildren().Count(x => !string.IsNullOrWhiteSpace(x["Url"]));
        var enableFmp = bool.TryParse(configuration["TranscriptProviders:EnableFmp"], out var fmpEnabled) && fmpEnabled;
        var enableFinnhub = bool.TryParse(configuration["TranscriptProviders:EnableFinnhub"], out var finnhubEnabled) && finnhubEnabled;
        var enableEarningsCallBiz = !bool.TryParse(configuration["TranscriptProviders:EarningsCallBiz:Enabled"], out var earningsCallBizEnabled) || earningsCallBizEnabled;

        return
        [
            new DataSourceStatusDto(
                "SEC EDGAR",
                !string.IsNullOrWhiteSpace(secOptions.Value.UserAgent),
                secLastImport.Where(x => x is not null).OrderByDescending(x => x).FirstOrDefault(),
                "Official SEC APIs are used with configured User-Agent. No API key required."),
            new DataSourceStatusDto(
                "Financial Modeling Prep transcripts",
                enableFmp && !string.IsNullOrWhiteSpace(fmpKey),
                fmpLastImport.Where(x => x is not null).OrderByDescending(x => x).FirstOrDefault(),
                enableFmp ? "FMP paid transcript provider is enabled." : "Paid provider disabled by default. Free-first providers are used instead."),
            new DataSourceStatusDto(
                "EarningsCallBiz transcripts",
                enableEarningsCallBiz,
                earningsCallBizLastImport.Where(x => x is not null).OrderByDescending(x => x).FirstOrDefault(),
                enableEarningsCallBiz ? "Public transcript provider enabled with cached, rate-limited requests." : "Public transcript provider disabled."),
            new DataSourceStatusDto(
                "Finnhub transcripts",
                enableFinnhub && !string.IsNullOrWhiteSpace(finnhubKey),
                null,
                enableFinnhub
                    ? "Finnhub paid transcript provider is enabled."
                    : "Paid provider disabled by default. Finnhub transcript endpoints require professional access."),
            new DataSourceStatusDto(
                "RSS/news feeds",
                feedCount > 0,
                rssLastImport.Where(x => x is not null).OrderByDescending(x => x).FirstOrDefault(),
                feedCount > 0 ? $"{feedCount} RSS/news feeds configured." : "No RSS/news feeds configured.")
        ];
    }
}
