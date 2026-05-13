using AiCapex.Application.Transcripts;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task EnsureSeededAsync(AiCapexDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var hadExistingCompanies = await db.Companies.AnyAsync(cancellationToken);
        var trackedCompanies = new[]
        {
            new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler" },
            new Company { Ticker = "AMZN", Name = "Amazon", Segment = "Hyperscaler" },
            new Company { Ticker = "GOOGL", Name = "Alphabet", Segment = "Hyperscaler" },
            new Company { Ticker = "META", Name = "Meta Platforms", Segment = "Hyperscaler" },
            new Company { Ticker = "ORCL", Name = "Oracle", Segment = "Cloud infrastructure" },
            new Company { Ticker = "NVDA", Name = "NVIDIA", Segment = "Accelerators" },
            new Company { Ticker = "AMD", Name = "Advanced Micro Devices", Segment = "Accelerators" },
            new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM" },
            new Company { Ticker = "SNDK", Name = "SanDisk", Segment = "Memory/HBM" },
            new Company { Ticker = "ASML", Name = "ASML", Segment = "Semicap" },
            new Company { Ticker = "TSM", Name = "TSMC", Segment = "Foundry/Packaging" },
            new Company { Ticker = "AVGO", Name = "Broadcom", Segment = "Networking/ASIC" },
            new Company { Ticker = "ANET", Name = "Arista Networks", Segment = "Networking" },
            new Company { Ticker = "VRT", Name = "Vertiv", Segment = "Power/Cooling" }
        };

        var existingTickers = await db.Companies.Select(x => x.Ticker).ToListAsync(cancellationToken);
        var missingCompanies = trackedCompanies
            .Where(company => !existingTickers.Contains(company.Ticker))
            .ToList();

        if (missingCompanies.Count > 0)
        {
            db.AddRange(missingCompanies);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (hadExistingCompanies)
        {
            return;
        }

        var companies = await db.Companies.ToListAsync(cancellationToken);
        var quarters = new[]
        {
            new FiscalQuarter { Year = 2025, Quarter = 3, PeriodEnd = new DateOnly(2025, 9, 30) },
            new FiscalQuarter { Year = 2025, Quarter = 4, PeriodEnd = new DateOnly(2025, 12, 31) },
            new FiscalQuarter { Year = 2026, Quarter = 1, PeriodEnd = new DateOnly(2026, 3, 31) }
        };

        db.AddRange(quarters);
        await db.SaveChangesAsync(cancellationToken);

        var latest = quarters[2];
        var previous = quarters[1];
        var metrics = new List<FinancialMetric>();
        foreach (var company in companies)
        {
            var baseCapex = company.Segment == "Hyperscaler" ? 18m : company.Ticker is "NVDA" or "TSM" ? 9m : 3m;
            metrics.AddRange([
                new FinancialMetric { CompanyId = company.Id, FiscalQuarterId = previous.Id, Kind = MetricKind.QuarterlyCapex, Value = baseCapex * 0.92m },
                new FinancialMetric { CompanyId = company.Id, FiscalQuarterId = latest.Id, Kind = MetricKind.QuarterlyCapex, Value = baseCapex },
                new FinancialMetric { CompanyId = company.Id, FiscalQuarterId = latest.Id, Kind = MetricKind.OperatingCashFlow, Value = baseCapex * 2.7m },
                new FinancialMetric { CompanyId = company.Id, FiscalQuarterId = latest.Id, Kind = MetricKind.FreeCashFlowEstimate, Value = baseCapex * 1.4m },
                new FinancialMetric { CompanyId = company.Id, FiscalQuarterId = latest.Id, Kind = MetricKind.CapexAsPercentOfOperatingCashFlow, Value = 37m, Unit = "%" }
            ]);
        }

        db.AddRange(metrics);

        db.AddRange(
            new RiskScoreSnapshot { FiscalQuarterId = quarters[0].Id, Score = 39, ChangeFromPreviousQuarter = -3, Band = "Healthy expansion", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6) },
            new RiskScoreSnapshot { FiscalQuarterId = previous.Id, Score = 47, ChangeFromPreviousQuarter = 8, Band = "Watch zone", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3) },
            new RiskScoreSnapshot { FiscalQuarterId = latest.Id, Score = 58, ChangeFromPreviousQuarter = 11, Band = "Watch zone", CreatedAt = DateTimeOffset.UtcNow }
        );

        var signals = new[]
        {
            Signal(null, latest, RiskScoreCategory.HyperscalerCapexRevisionTrend, "Hyperscaler capex guidance mostly hold after prior raises", SignalDirection.Bearish, 32, "MSFT, AMZN, GOOGL, and META still expand, but revision momentum cooled."),
            Signal(companies.Single(x => x.Ticker == "MU"), latest, RiskScoreCategory.HbmDramPricingAllocation, "HBM allocation remains tight", SignalDirection.Bullish, -28, "HBM demand exceeds supply and long-term agreements support pricing."),
            Signal(companies.Single(x => x.Ticker == "TSM"), latest, RiskScoreCategory.CowosAdvancedPackaging, "CoWoS bottleneck improving gradually", SignalDirection.Neutral, 4, "Advanced packaging capacity is still gating some deployments, but expansion continues."),
            Signal(companies.Single(x => x.Ticker == "VRT"), latest, RiskScoreCategory.DataCenterPower, "Power availability delays selected data center ramps", SignalDirection.Bearish, 42, "Grid constraints and substation timelines are increasingly visible."),
            Signal(companies.Single(x => x.Ticker == "NVDA"), latest, RiskScoreCategory.AiRevenueMonetization, "Inference monetization evidence broadens", SignalDirection.Bullish, -18, "AI revenue commentary remains constructive."),
            Signal(null, latest, RiskScoreCategory.FinancialStressFreeCashFlow, "Capex intensity consumes more OCF", SignalDirection.Bearish, 36, "Capex as a percent of operating cash flow is elevated across hyperscalers.")
        };
        db.AddRange(signals);

        var analyzer = new KeywordTranscriptAnalyzer();
        var transcriptText = "AI infrastructure and GPU capacity remain supply constrained. HBM demand exceeds supply. Advanced packaging capacity and power availability are key constraints, though management does not see a digestion period.";
        var transcript = new Transcript
        {
            CompanyId = companies.Single(x => x.Ticker == "NVDA").Id,
            FiscalQuarterId = latest.Id,
            PublishedDate = new DateOnly(2026, 4, 22),
            Title = "NVIDIA Q1 2026 earnings call sample",
            Text = transcriptText
        };
        db.Add(transcript);
        await db.SaveChangesAsync(cancellationToken);

        db.AddRange(analyzer.Analyze(transcriptText).Select(x => new TranscriptMention
        {
            TranscriptId = transcript.Id,
            KeywordGroup = x.Group,
            Keyword = x.Keyword,
            Count = x.Count
        }));

        db.AddRange(
            new SourceDocument { CompanyId = companies.Single(x => x.Ticker == "MSFT").Id, SourceType = SourceType.SecXbrl, Title = "Sample 10-Q XBRL capex extraction", Url = "https://www.sec.gov/", Summary = "Seeded SEC-style capex and OCF metric.", PublishedDate = latest.PeriodEnd },
            new SourceDocument { CompanyId = companies.Single(x => x.Ticker == "VRT").Id, SourceType = SourceType.NewsRss, Title = "Sample power equipment backlog update", Url = "https://example.com/power-backlog", Summary = "Power chain backlog supports continued data center buildouts.", PublishedDate = new DateOnly(2026, 4, 15) }
        );

        db.AddRange(
            new WatchlistAlert { Severity = AlertSeverity.Warning, Title = "Risk score rose 11 points", Message = "Current score increased from 47 to 58 quarter over quarter.", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
            new WatchlistAlert { Severity = AlertSeverity.Warning, Title = "Power commentary worsened", Message = "Power availability and grid constraint mentions rose materially.", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) }
        );

        await db.SaveChangesAsync(cancellationToken);
    }

    private static IndicatorSignal Signal(Company? company, FiscalQuarter quarter, RiskScoreCategory category, string name, SignalDirection direction, decimal impact, string summary) =>
        new()
        {
            CompanyId = company?.Id,
            FiscalQuarterId = quarter.Id,
            Category = category,
            Name = name,
            Direction = direction,
            ScoreImpact = impact,
            Summary = summary
        };
}
