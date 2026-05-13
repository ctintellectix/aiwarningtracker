using AiCapex.Application.Dashboard;
using AiCapex.Application.Services;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Services;

public sealed class AiCapexDataService(AiCapexDbContext db) : IAiCapexReadService, IManualEntryService
{
    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var history = await GetRiskScoreHistoryAsync(cancellationToken);
        var latest = history.Last();
        var signals = await SignalQuery().ToListAsync(cancellationToken);
        var categories = await GetIndicatorTrendsAsync(cancellationToken);

        return new DashboardSummaryDto(
            latest.Score,
            latest.Change,
            latest.Band,
            "HBM allocation, networking demand, and inference monetization remain constructive.",
            "Power constraints, capex intensity, and cooled hyperscaler revisions are the main pressure points.",
            signals.Where(x => x.Direction == SignalDirection.Bullish).OrderBy(x => x.ScoreImpact).Take(4).ToList(),
            signals.Where(x => x.Direction == SignalDirection.Bearish).OrderByDescending(x => x.ScoreImpact).Take(4).ToList(),
            categories,
            history);
    }

    public async Task<IReadOnlyList<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Companies
            .OrderBy(x => x.Ticker)
            .Select(x => new CompanyDto(
                x.Id,
                x.Ticker,
                x.Name,
                x.Segment,
                db.IndicatorSignals.Where(s => s.CompanyId == x.Id).Select(s => (int?)s.ScoreImpact).Average() ?? 0))
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Ticker == ticker.ToUpper(), cancellationToken);
        if (company is null)
        {
            return null;
        }

        var dto = new CompanyDto(company.Id, company.Ticker, company.Name, company.Segment,
            await db.IndicatorSignals.Where(x => x.CompanyId == company.Id).Select(x => (int?)x.ScoreImpact).AverageAsync(cancellationToken) ?? 0);

        var metrics = await GetCompanyMetricsAsync(company.Ticker, cancellationToken);
        var signals = await db.IndicatorSignals
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Where(x => x.CompanyId == company.Id)
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                x.Direction,
                x.ScoreImpact,
                x.Summary))
            .ToListAsync(cancellationToken);

        var sources = await db.SourceDocuments
            .Include(x => x.Company)
            .Where(x => x.CompanyId == company.Id)
            .Select(x => new SourceDocumentDto(
                x.Company == null ? null : x.Company.Ticker,
                x.SourceType,
                x.Title,
                x.Url,
                x.Summary,
                x.PublishedDate))
            .ToListAsync(cancellationToken);

        return new CompanyDetailDto(dto, metrics, signals, sources);
    }

    public async Task<IReadOnlyList<MetricDto>> GetCompanyMetricsAsync(string ticker, CancellationToken cancellationToken = default)
    {
        return await db.FinancialMetrics
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Where(x => x.Company != null && x.Company.Ticker == ticker.ToUpper())
            .OrderBy(x => x.FiscalQuarter!.Year)
            .ThenBy(x => x.FiscalQuarter!.Quarter)
            .Select(x => new MetricDto(x.FiscalQuarter!.Label, x.Kind.ToString(), x.Value, x.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryStatusDto>> GetIndicatorTrendsAsync(CancellationToken cancellationToken = default)
    {
        var signals = await db.IndicatorSignals.ToListAsync(cancellationToken);
        return signals
            .GroupBy(x => x.Category)
            .Select(x => new CategoryStatusDto(
                x.Key,
                Math.Round(x.Average(v => v.ScoreImpact), 1),
                x.Average(v => v.ScoreImpact) > 25 ? "Weakening" : x.Average(v => v.ScoreImpact) < -15 ? "Constructive" : "Mixed",
                x.OrderByDescending(v => Math.Abs(v.ScoreImpact)).Select(v => v.Summary).First()))
            .OrderByDescending(x => Math.Abs(x.AverageSignal))
            .ToList();
    }

    public async Task<IReadOnlyList<TranscriptSignalDto>> GetTranscriptSignalsAsync(CancellationToken cancellationToken = default)
    {
        return await db.TranscriptMentions
            .Include(x => x.Transcript)!.ThenInclude(x => x!.Company)
            .Include(x => x.Transcript)!.ThenInclude(x => x!.FiscalQuarter)
            .OrderByDescending(x => x.Count)
            .Select(x => new TranscriptSignalDto(
                x.Transcript!.Company!.Ticker,
                x.Transcript.FiscalQuarter!.Label,
                x.Transcript.Title,
                x.KeywordGroup,
                x.Count,
                x.Transcript.PublishedDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuarterScoreDto>> GetRiskScoreHistoryAsync(CancellationToken cancellationToken = default)
    {
        return await db.RiskScoreSnapshots
            .Include(x => x.FiscalQuarter)
            .OrderBy(x => x.FiscalQuarter!.Year)
            .ThenBy(x => x.FiscalQuarter!.Quarter)
            .Select(x => new QuarterScoreDto(x.FiscalQuarter!.Label, x.Score, x.ChangeFromPreviousQuarter, x.Band))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        var alerts = await db.WatchlistAlerts.ToListAsync(cancellationToken);
        return alerts
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AlertDto(x.Id, x.Severity, x.Title, x.Message, x.CreatedAt, x.IsAcknowledged))
            .ToList();
    }

    public async Task<SignalDto> AddManualEntryAsync(ManualEntryRequest request, CancellationToken cancellationToken = default)
    {
        var ticker = request.Ticker.ToUpper();
        var company = await db.Companies.SingleAsync(x => x.Ticker == ticker, cancellationToken);
        var quarter = await db.FiscalQuarters.OrderByDescending(x => x.Year).ThenByDescending(x => x.Quarter).FirstAsync(cancellationToken);
        var category = Enum.TryParse<RiskScoreCategory>(request.Category, true, out var parsed)
            ? parsed
            : RiskScoreCategory.FinancialStressFreeCashFlow;

        var direction = request.ScoreImpact switch
        {
            < -5 => SignalDirection.Bullish,
            > 5 => SignalDirection.Bearish,
            _ => SignalDirection.Neutral
        };

        var signal = new IndicatorSignal
        {
            CompanyId = company.Id,
            FiscalQuarterId = quarter.Id,
            Category = category,
            Name = request.SourceTitle,
            Direction = direction,
            ScoreImpact = request.ScoreImpact,
            Summary = request.Summary
        };

        db.IndicatorSignals.Add(signal);
        db.SourceDocuments.Add(new SourceDocument
        {
            CompanyId = company.Id,
            SourceType = SourceType.ManualEntry,
            Title = request.SourceTitle,
            Summary = request.Summary,
            Url = "",
            PublishedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        await db.SaveChangesAsync(cancellationToken);

        return new SignalDto(company.Ticker, quarter.Label, category, signal.Name, direction, signal.ScoreImpact, signal.Summary);
    }

    private IQueryable<SignalDto> SignalQuery()
    {
        return db.IndicatorSignals
            .Include(x => x.Company)
            .Include(x => x.FiscalQuarter)
            .Select(x => new SignalDto(
                x.Company == null ? null : x.Company.Ticker,
                x.FiscalQuarter!.Label,
                x.Category,
                x.Name,
                x.Direction,
                x.ScoreImpact,
                x.Summary));
    }

    private IQueryable<SourceDocumentDto> SourceQuery()
    {
        return db.SourceDocuments
            .Include(x => x.Company)
            .Select(x => new SourceDocumentDto(
                x.Company == null ? null : x.Company.Ticker,
                x.SourceType,
                x.Title,
                x.Url,
                x.Summary,
                x.PublishedDate));
    }
}
