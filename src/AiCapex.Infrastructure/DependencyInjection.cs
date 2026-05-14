using AiCapex.Application.Ingestion;
using AiCapex.Application.Services;
using AiCapex.Infrastructure.Alerts;
using AiCapex.Infrastructure.Ingestion;
using AiCapex.Infrastructure.News;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Scoring;
using AiCapex.Infrastructure.Sec;
using AiCapex.Infrastructure.Services;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AiCapex") ?? "Data Source=ai-capex-monitor.db";
        var secOptions = new SecOptions
        {
            UserAgent = configuration["Sec:UserAgent"] ?? "AiCapexSlowdownMonitor/1.0 (contact@example.com)",
            CacheDirectory = configuration["Sec:CacheDirectory"] ?? "data/sec-cache"
        };
        secOptions.UserAgent = configuration["SEC_USER_AGENT"] ?? secOptions.UserAgent;
        var fmpOptions = new FmpOptions
        {
            Enabled = bool.TryParse(configuration["TranscriptProviders:EnableFmp"], out var enableFmp) && enableFmp,
            ApiKey = configuration["FMP_API_KEY"],
            BaseUrl = configuration["Fmp:BaseUrl"] ?? "https://financialmodelingprep.com/stable"
        };
        var finnhubOptions = new FinnhubOptions
        {
            Enabled = bool.TryParse(configuration["TranscriptProviders:EnableFinnhub"], out var enableFinnhub) && enableFinnhub,
            ApiKey = configuration["FINNHUB_API_KEY"],
            BaseUrl = configuration["Finnhub:BaseUrl"] ?? "https://finnhub.io/api/v1"
        };
        var earningsCallBizOptions = new EarningsCallBizOptions
        {
            Enabled = !bool.TryParse(configuration["TranscriptProviders:EarningsCallBiz:Enabled"], out var enableEarningsCallBiz) || enableEarningsCallBiz,
            BaseUrl = configuration["TranscriptProviders:EarningsCallBiz:BaseUrl"] ?? "https://earningscall.biz",
            UserAgent = configuration["TranscriptProviders:EarningsCallBiz:UserAgent"] ?? "AiCapexMonitor/1.0 (contact@example.com)",
            CacheDays = int.TryParse(configuration["TranscriptProviders:EarningsCallBiz:CacheDays"], out var cacheDays) ? cacheDays : 7,
            RequestDelayMs = int.TryParse(configuration["TranscriptProviders:EarningsCallBiz:RequestDelayMs"], out var delayMs) ? delayMs : 1000
        };
        var feeds = configuration.GetSection("NewsFeeds").GetChildren()
            .Select(section => new RssFeedOptions
            {
                Name = section["Name"] ?? "Configured feed",
                Url = section["Url"] ?? "",
                CredibilityWeight = decimal.TryParse(section["CredibilityWeight"], out var weight) ? weight : 0.7m
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .ToList();
        services.AddSingleton(Options.Create(secOptions));
        services.AddSingleton(Options.Create(fmpOptions));
        services.AddSingleton(Options.Create(finnhubOptions));
        services.AddSingleton(Options.Create(earningsCallBizOptions));
        services.AddSingleton<IReadOnlyList<RssFeedOptions>>(feeds);
        services.AddMemoryCache();
        services.AddHttpClient<EarningsCallBizTranscriptProvider>(client =>
        {
            client.BaseAddress = new Uri(earningsCallBizOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", earningsCallBizOptions.UserAgent);
        });
        services.AddSingleton(_ =>
        {
            return new HttpClient();
        });
        services.AddDbContext<AiCapexDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ISecClient, SecClient>();
        services.AddScoped<IAiCapexReadService, AiCapexDataService>();
        services.AddScoped<IManualEntryService, AiCapexDataService>();
        services.AddScoped<IDataSourceStatusService, DataSourceStatusService>();
        services.AddScoped<ISecTickerCikMapper, SecTickerCikMapper>();
        services.AddScoped<ISecCompanyFactImporter, SecCompanyFactImporter>();
        services.AddScoped<AiCapex.Application.Scoring.IRiskScoringService, RiskScoringService>();
        services.AddScoped<AiCapex.Application.Alerts.IAlertGenerationService, AlertGenerationService>();
        services.AddScoped<CachedTranscriptProvider>();
        services.AddScoped<TranscriptStorageService>();
        services.AddScoped<ITranscriptStorageService>(provider => provider.GetRequiredService<TranscriptStorageService>());
        services.AddSingleton<ITranscriptImportClock, SystemTranscriptImportClock>();
        services.AddScoped<ITranscriptImportService, TranscriptImportService>();
        services.AddScoped<FmpTranscriptProvider>();
        services.AddScoped<FinnhubTranscriptProvider>();
        services.AddScoped<ITranscriptProviderChain>(provider =>
        {
            var priority = configuration.GetSection("TranscriptProviders:ProviderPriority").GetChildren().Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (priority.Length == 0)
            {
                priority = ["Cached"];
            }

            var providers = new List<ITranscriptProvider>();
            foreach (var name in priority)
            {
                switch (name)
                {
                    case "Cached":
                        providers.Add(provider.GetRequiredService<CachedTranscriptProvider>());
                        break;
                    case "EarningsCallBiz":
                        providers.Add(provider.GetRequiredService<EarningsCallBizTranscriptProvider>());
                        break;
                }
            }

            if (earningsCallBizOptions.Enabled && !providers.OfType<EarningsCallBizTranscriptProvider>().Any())
            {
                providers.Add(provider.GetRequiredService<EarningsCallBizTranscriptProvider>());
            }

            if (fmpOptions.Enabled)
            {
                providers.Add(provider.GetRequiredService<FmpTranscriptProvider>());
            }

            if (finnhubOptions.Enabled)
            {
                providers.Add(provider.GetRequiredService<FinnhubTranscriptProvider>());
            }

            return new TranscriptProviderChain(providers);
        });
        services.AddScoped<IWhisperTranscriptProvider, WhisperTranscriptProvider>();
        services.AddScoped<IRssFeedClient, RssFeedClient>();
        services.AddScoped<IRssImportService, RssImportService>();
        services.AddScoped<ISecXbrlFinancialDataIngestor, StubSecXbrlFinancialDataIngestor>();
        services.AddScoped<ITranscriptIngestor, StubTranscriptIngestor>();
        services.AddScoped<IManualIndicatorIngestor, StubManualIndicatorIngestor>();
        services.AddScoped<INewsSourceIngestor, StubNewsSourceIngestor>();
        return services;
    }
}
