using AiCapex.Application.Ingestion;
using AiCapex.Application.Services;
using AiCapex.Infrastructure.Alerts;
using AiCapex.Infrastructure.Analysis;
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
using AiCapex.Domain.Scoring;
using AiCapex.Application.Alerts;

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
        var earningsCallBizOptions = new EarningsCallBizOptions
        {
            Enabled = !bool.TryParse(configuration["TranscriptProviders:EarningsCallBiz:Enabled"], out var enableEarningsCallBiz) || enableEarningsCallBiz,
            BaseUrl = configuration["TranscriptProviders:EarningsCallBiz:BaseUrl"] ?? "https://earningscall.biz",
            UserAgent = configuration["TranscriptProviders:EarningsCallBiz:UserAgent"] ?? "AiCapexMonitor/1.0 (contact@example.com)",
            CacheDays = int.TryParse(configuration["TranscriptProviders:EarningsCallBiz:CacheDays"], out var cacheDays) ? cacheDays : 7,
            RequestDelayMs = int.TryParse(configuration["TranscriptProviders:EarningsCallBiz:RequestDelayMs"], out var delayMs) ? delayMs : 1000
        };
        var openAiOptions = new OpenAiOptions
        {
            Enabled = bool.TryParse(configuration["OpenAI:Enabled"], out var enableOpenAi) && enableOpenAi,
            ApiKey = configuration["OPENAI_API_KEY"],
            BaseUrl = configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/",
            Model = configuration["OpenAI:Model"] ?? "gpt-5.4-mini"
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
        var riskScoreWeights = new RiskScoreWeights(
            ParseDecimal(configuration["RiskScoreWeights:HyperscalerCapexRevisionTrend"], 30m),
            ParseDecimal(configuration["RiskScoreWeights:HbmDramPricingAllocation"], 20m),
            ParseDecimal(configuration["RiskScoreWeights:CowosAdvancedPackaging"], 15m),
            ParseDecimal(configuration["RiskScoreWeights:DataCenterPower"], 15m),
            ParseDecimal(configuration["RiskScoreWeights:AiRevenueMonetization"], 10m),
            ParseDecimal(configuration["RiskScoreWeights:FinancialStressFreeCashFlow"], 10m));
        var alertThresholds = new AlertThresholdOptions
        {
            ScoreDeteriorationPoints = ParseInt(configuration["AlertThresholds:RiskScoreIncreasePoints"], 10),
            CapexOcfStressPercent = ParseDecimal(configuration["AlertThresholds:CapexAsPercentOfOperatingCashFlow"], 75m),
            CategoryWeakeningSignal = ParseDecimal(configuration["AlertThresholds:CategoryWeakeningSignal"], -3m)
        };
        services.AddSingleton(Options.Create(secOptions));
        services.AddSingleton(Options.Create(earningsCallBizOptions));
        services.AddSingleton(Options.Create(openAiOptions));
        services.AddSingleton<IReadOnlyList<RssFeedOptions>>(feeds);
        services.AddSingleton(riskScoreWeights);
        services.AddSingleton(alertThresholds);
        services.AddMemoryCache();
        services.AddHttpClient<EarningsCallBizTranscriptProvider>(client =>
        {
            client.BaseAddress = new Uri(earningsCallBizOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", earningsCallBizOptions.UserAgent);
        });
        services.AddHttpClient<DocumentNarrativeAnalysisService>(client =>
        {
            client.BaseAddress = new Uri(openAiOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(90);
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
        services.AddScoped<AiCapex.Application.Analysis.IDocumentNarrativeAnalysisService>(provider =>
            provider.GetRequiredService<DocumentNarrativeAnalysisService>());
        services.AddScoped<CachedTranscriptProvider>();
        services.AddScoped<TranscriptStorageService>();
        services.AddScoped<ITranscriptStorageService>(provider => provider.GetRequiredService<TranscriptStorageService>());
        services.AddSingleton<ITranscriptImportClock, SystemTranscriptImportClock>();
        services.AddScoped<ITranscriptImportService, TranscriptImportService>();
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

    private static decimal ParseDecimal(string? value, decimal fallback) =>
        decimal.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
