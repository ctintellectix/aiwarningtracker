using AiCapex.Application.Ingestion;
using AiCapex.Application.Services;
using AiCapex.Infrastructure.Ingestion;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiCapex.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AiCapex") ?? "Data Source=ai-capex-monitor.db";
        services.AddDbContext<AiCapexDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAiCapexReadService, AiCapexDataService>();
        services.AddScoped<IManualEntryService, AiCapexDataService>();
        services.AddScoped<ISecXbrlFinancialDataIngestor, StubSecXbrlFinancialDataIngestor>();
        services.AddScoped<ITranscriptIngestor, StubTranscriptIngestor>();
        services.AddScoped<IManualIndicatorIngestor, StubManualIndicatorIngestor>();
        services.AddScoped<INewsSourceIngestor, StubNewsSourceIngestor>();
        return services;
    }
}
