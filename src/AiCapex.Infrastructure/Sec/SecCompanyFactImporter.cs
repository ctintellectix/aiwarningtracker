using AiCapex.Application.Dashboard;
using AiCapex.Application.Ingestion;
using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Sec;

public sealed class SecCompanyFactImporter(AiCapexDbContext db, ISecTickerCikMapper cikMapper, ISecClient secClient) : ISecCompanyFactImporter
{
    public async Task<SecImportResultDto> ImportAsync(string ticker, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        await SchemaUpgrade.EnsureCompatibleSchemaAsync(db, cancellationToken);
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Ticker == ticker, cancellationToken);
        if (company is null)
        {
            return new SecImportResultDto(ticker, false, 0, 0, "Company is not tracked.");
        }

        var cik = await cikMapper.GetCikAsync(ticker, cancellationToken);
        if (string.IsNullOrWhiteSpace(cik))
        {
            return new SecImportResultDto(ticker, false, 0, 0, "No SEC CIK found. This may be a foreign issuer, renamed ticker, or unsupported company.");
        }

        var (json, usedLiveData, sourceUrl) = await secClient.GetCompanyFactsAsync(cik, ticker, cancellationToken);
        var facts = SecCompanyFactsParser.Parse(json, sourceUrl);
        var metrics = SecMetricExtractor.Extract(facts);

        db.CompanyFacts.RemoveRange(db.CompanyFacts.Where(x => x.CompanyId == company.Id));
        db.SecFilings.RemoveRange(db.SecFilings.Where(x => x.CompanyId == company.Id));
        db.FinancialMetrics.RemoveRange(db.FinancialMetrics.Where(x => x.CompanyId == company.Id && x.Source == "SEC EDGAR"));
        db.SourceDocuments.RemoveRange(db.SourceDocuments.Where(x =>
            x.CompanyId == company.Id &&
            x.SourceType == SourceType.SecXbrl &&
            x.Provider == "SEC EDGAR"));
        await db.SaveChangesAsync(cancellationToken);

        db.CompanyFacts.AddRange(facts.Select(x => new CompanyFact
        {
            CompanyId = company.Id,
            Taxonomy = x.Taxonomy,
            Tag = x.Tag,
            Unit = x.Unit,
            FiscalYear = x.FiscalYear,
            FiscalPeriod = x.FiscalPeriod,
            Form = x.Form,
            FiledDate = x.FiledDate,
            EndDate = x.EndDate,
            Value = x.Value,
            SourceUrl = x.SourceUrl
        }));

        db.SecFilings.AddRange(facts
            .Where(x => !string.IsNullOrWhiteSpace(x.AccessionNumber))
            .GroupBy(x => x.AccessionNumber!)
            .Select(x =>
            {
                var first = x.OrderByDescending(f => f.FiledDate).First();
                return new SecFiling
                {
                    CompanyId = company.Id,
                    AccessionNumber = x.Key,
                    FilingType = first.Form,
                    FilingDate = first.FiledDate,
                    PeriodEndDate = first.EndDate,
                    SecUrl = first.SourceUrl,
                    RawJsonPath = sourceUrl,
                    ParsedAtUtc = DateTimeOffset.UtcNow
                };
            }));

        foreach (var metric in metrics)
        {
            var quarter = await EnsureFiscalQuarterAsync(metric.FiscalYear, metric.FiscalQuarter, cancellationToken);
            db.FinancialMetrics.Add(new FinancialMetric
            {
                CompanyId = company.Id,
                FiscalQuarterId = quarter.Id,
                Kind = ToMetricKind(metric.MetricName),
                FiscalYear = metric.FiscalYear,
                FiscalQuarterNumber = metric.FiscalQuarter,
                PeriodEndDate = metric.PeriodEndDate,
                SourcePeriodLabel = $"{company.Ticker} FY{metric.FiscalYear} Q{metric.FiscalQuarter}",
                MetricName = metric.MetricName,
                Value = metric.Value,
                Unit = metric.Unit,
                Source = "SEC EDGAR",
                SourceUrl = metric.SourceUrl
            });
        }

        db.SourceDocuments.Add(new SourceDocument
        {
            CompanyId = company.Id,
            SourceType = SourceType.SecXbrl,
            Provider = "SEC EDGAR",
            Title = $"{ticker} SEC companyfacts",
            Url = sourceUrl,
            Summary = $"Imported {facts.Count} SEC XBRL facts and {metrics.Count} normalized metrics.",
            PublishedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PublishedAtUtc = DateTimeOffset.UtcNow,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            RawText = json.Length > 8000 ? json[..8000] : json,
            CredibilityWeight = 1m
        });

        await db.SaveChangesAsync(cancellationToken);
        return new SecImportResultDto(ticker, usedLiveData, facts.Count, metrics.Count, usedLiveData ? "Imported live SEC companyfacts." : "Imported cached SEC companyfacts.");
    }

    private async Task<FiscalQuarter> EnsureFiscalQuarterAsync(int year, int quarterNumber, CancellationToken cancellationToken)
    {
        var quarter = await db.FiscalQuarters.SingleOrDefaultAsync(x => x.Year == year && x.Quarter == quarterNumber, cancellationToken);
        if (quarter is not null)
        {
            return quarter;
        }

        quarter = new FiscalQuarter { Year = year, Quarter = quarterNumber, PeriodEnd = new DateOnly(year, quarterNumber * 3, DateTime.DaysInMonth(year, quarterNumber * 3)) };
        db.FiscalQuarters.Add(quarter);
        await db.SaveChangesAsync(cancellationToken);
        return quarter;
    }

    private static MetricKind ToMetricKind(string metricName) => metricName switch
    {
        "Quarterly Capex" => MetricKind.QuarterlyCapex,
        "Operating Cash Flow" => MetricKind.OperatingCashFlow,
        "Capex / OCF" => MetricKind.CapexAsPercentOfOperatingCashFlow,
        "Revenue" => MetricKind.Revenue,
        "Debt" => MetricKind.Debt,
        "TTM Capex" => MetricKind.TtmCapex,
        "Capex QoQ Growth" => MetricKind.CapexQoqGrowth,
        "Capex YoY Growth" => MetricKind.CapexYoyGrowth,
        _ => MetricKind.QuarterlyCapex
    };
}
