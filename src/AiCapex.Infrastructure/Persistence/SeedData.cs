using AiCapex.Application.Transcripts;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Persistence;

public static class SeedData
{
    private static readonly string[] SeedSignalNames =
    [
        "Hyperscaler capex guidance mostly hold after prior raises",
        "HBM allocation remains tight",
        "CoWoS bottleneck improving gradually",
        "Power availability delays selected data center ramps",
        "Inference monetization evidence broadens",
        "Capex intensity consumes more OCF"
    ];

    private static readonly string[] SeedAlertTitles =
    [
        "Risk score rose 11 points",
        "Power commentary worsened"
    ];

    public static Task EnsureSeededAsync(AiCapexDbContext db, CancellationToken cancellationToken = default) =>
        EnsureSeededAsync(db, new SeedDataOptions(), cancellationToken);

    public static async Task EnsureSeededAsync(AiCapexDbContext db, SeedDataOptions options, CancellationToken cancellationToken = default)
    {
        await SchemaUpgrade.EnsureCompatibleSchemaAsync(db, cancellationToken);

        var hadExistingCompanies = await db.Companies.AnyAsync(cancellationToken);
        var trackedCompanies = new[]
        {
            Company("MSFT", "Microsoft", "Hyperscaler", "0000789019", "nasdaq", isHyperscaler: true),
            Company("AMZN", "Amazon", "Hyperscaler", "0001018724", "nasdaq", isHyperscaler: true),
            Company("GOOGL", "Alphabet", "Hyperscaler", "0001652044", "nasdaq", isHyperscaler: true),
            Company("META", "Meta Platforms", "Hyperscaler", "0001326801", "nasdaq", isHyperscaler: true),
            Company("ORCL", "Oracle", "Cloud infrastructure", "0001341439", "nyse", isHyperscaler: true),
            Company("NVDA", "NVIDIA", "Accelerators", "0001045810", "nasdaq", isSemiconductor: true),
            Company("AMD", "Advanced Micro Devices", "Accelerators", "0000002488", "nasdaq", isSemiconductor: true),
            Company("AVGO", "Broadcom", "Networking/ASIC", "0001730168", "nasdaq", isSemiconductor: true),
            Company("MU", "Micron", "Memory/HBM", "0000723125", "nasdaq", isSemiconductor: true),
            Company("SNDK", "SanDisk", "Memory/HBM", null, "nasdaq", isSemiconductor: true),
            Company("ASML", "ASML", "Semicap", null, "nasdaq", isSemiconductor: true),
            Company("TSM", "TSMC", "Foundry/Packaging", null, "nyse", isSemiconductor: true),
            Company("ANET", "Arista Networks", "Networking", "0001596532", "nyse", isDataCenterInfrastructure: true),
            Company("VRT", "Vertiv", "Power/Cooling", "0001674101", "nyse", isDataCenterInfrastructure: true),
            Company("SMCI", "Super Micro Computer", "AI servers", "0001375365", "nasdaq", isDataCenterInfrastructure: true),
            Company("DELL", "Dell Technologies", "AI servers", "0001571996", "nyse", isDataCenterInfrastructure: true),
            Company("MRVL", "Marvell Technology", "Networking/ASIC", "0001835632", "nasdaq", isSemiconductor: true)
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

        foreach (var tracked in trackedCompanies)
        {
            var existing = await db.Companies.SingleAsync(x => x.Ticker == tracked.Ticker, cancellationToken);
            existing.Cik ??= tracked.Cik;
            existing.Sector ??= tracked.Sector;
            existing.Industry ??= tracked.Industry;
            existing.ExchangeMarket ??= tracked.ExchangeMarket;
            existing.IsHyperscaler = tracked.IsHyperscaler;
            existing.IsSemiconductor = tracked.IsSemiconductor;
            existing.IsDataCenterInfrastructure = tracked.IsDataCenterInfrastructure;
        }
        await db.SaveChangesAsync(cancellationToken);

        if (!options.UseSampleData)
        {
            if (options.PurgeSampleDataWhenDisabled)
            {
                await PurgeSampleDataAsync(db, cancellationToken);
            }

            return;
        }

        if (hadExistingCompanies)
        {
            var existingCompaniesForBackfill = await db.Companies.ToListAsync(cancellationToken);
            var latestQuarter = await EnsureFiscalQuarterAsync(db, 2026, 1, cancellationToken);
            await EnsureSampleTranscriptsAsync(db, existingCompaniesForBackfill, latestQuarter, cancellationToken);
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
            new RiskScoreSnapshot { FiscalQuarterId = quarters[0].Id, Score = 39, ChangeFromPreviousQuarter = -3, Band = "Weak", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6) },
            new RiskScoreSnapshot { FiscalQuarterId = previous.Id, Score = 47, ChangeFromPreviousQuarter = 8, Band = "Neutral", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3) },
            new RiskScoreSnapshot { FiscalQuarterId = latest.Id, Score = 58, ChangeFromPreviousQuarter = 11, Band = "Neutral", CreatedAt = DateTimeOffset.UtcNow }
        );

        var signals = new[]
        {
            Signal(null, latest, RiskScoreCategory.HyperscalerCapexRevisionTrend, "Hyperscaler capex guidance mostly hold after prior raises", SignalDirection.Bearish, -32, "MSFT, AMZN, GOOGL, and META still expand, but revision momentum cooled."),
            Signal(companies.Single(x => x.Ticker == "MU"), latest, RiskScoreCategory.HbmDramPricingAllocation, "HBM allocation remains tight", SignalDirection.Bullish, 28, "HBM demand exceeds supply and long-term agreements support pricing."),
            Signal(companies.Single(x => x.Ticker == "TSM"), latest, RiskScoreCategory.CowosAdvancedPackaging, "CoWoS bottleneck improving gradually", SignalDirection.Neutral, 4, "Advanced packaging capacity is still gating some deployments, but expansion continues."),
            Signal(companies.Single(x => x.Ticker == "VRT"), latest, RiskScoreCategory.DataCenterPower, "Power availability delays selected data center ramps", SignalDirection.Bearish, -42, "Grid constraints and substation timelines are increasingly visible."),
            Signal(companies.Single(x => x.Ticker == "NVDA"), latest, RiskScoreCategory.AiRevenueMonetization, "Inference monetization evidence broadens", SignalDirection.Bullish, 18, "AI revenue commentary remains constructive."),
            Signal(null, latest, RiskScoreCategory.FinancialStressFreeCashFlow, "Capex intensity consumes more OCF", SignalDirection.Bearish, -36, "Capex as a percent of operating cash flow is elevated across hyperscalers.")
        };
        db.AddRange(signals);

        await EnsureSampleTranscriptsAsync(db, companies, latest, cancellationToken);

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

    private static Company Company(
        string ticker,
        string name,
        string segment,
        string? cik,
        string? exchangeMarket,
        bool isHyperscaler = false,
        bool isSemiconductor = false,
        bool isDataCenterInfrastructure = false) =>
        new()
        {
            Ticker = ticker,
            Name = name,
            Segment = segment,
            Cik = cik,
            Sector = isHyperscaler ? "Technology" : isSemiconductor ? "Semiconductors" : "Industrials",
            Industry = segment,
            ExchangeMarket = exchangeMarket,
            IsHyperscaler = isHyperscaler,
            IsSemiconductor = isSemiconductor,
            IsDataCenterInfrastructure = isDataCenterInfrastructure
        };

    private static async Task<FiscalQuarter> EnsureFiscalQuarterAsync(AiCapexDbContext db, int year, int quarterNumber, CancellationToken cancellationToken)
    {
        var quarter = await db.FiscalQuarters.SingleOrDefaultAsync(x => x.Year == year && x.Quarter == quarterNumber, cancellationToken);
        if (quarter is not null)
        {
            return quarter;
        }

        quarter = new FiscalQuarter
        {
            Year = year,
            Quarter = quarterNumber,
            PeriodEnd = new DateOnly(year, quarterNumber * 3, DateTime.DaysInMonth(year, quarterNumber * 3))
        };
        db.FiscalQuarters.Add(quarter);
        await db.SaveChangesAsync(cancellationToken);
        return quarter;
    }

    private static async Task EnsureSampleTranscriptsAsync(AiCapexDbContext db, IReadOnlyList<Company> companies, FiscalQuarter latest, CancellationToken cancellationToken)
    {
        var samples = new[]
        {
            Sample("MSFT", "Microsoft Q1 2026 AI infrastructure sample", "AI infrastructure and data center investment remain a priority. Capital expenditures and property and equipment spending support inference and compute capacity, while power availability is a constraint."),
            Sample("AMZN", "Amazon Q1 2026 infrastructure sample", "Capital expenditures include data center investment and infrastructure spend for accelerated computing. Demand exceeds supply in selected regions, but management continues to optimize spend."),
            Sample("GOOGL", "Alphabet Q1 2026 AI infrastructure sample", "AI infrastructure, training cluster capacity, and inference demand are driving data center investment. Power availability and cooling constraint timelines affect some deployments."),
            Sample("META", "Meta Q1 2026 capex sample", "Capex and data center investment remain elevated for training cluster and inference workloads. GPU capacity is capacity constrained, although utilization and monetization are closely watched."),
            Sample("ORCL", "Oracle Q1 2026 cloud infrastructure sample", "Backlog and long-term agreement activity support cloud infrastructure spend. Data center power availability and GPU capacity influence customer deployment timing."),
            Sample("NVDA", "NVIDIA Q1 2026 earnings call sample", "AI infrastructure and GPU capacity remain supply constrained. HBM demand exceeds supply. Advanced packaging capacity and power availability are key constraints, though management does not see a digestion period."),
            Sample("AMD", "AMD Q1 2026 accelerator sample", "Inference and accelerated computing demand support GPU capacity plans. HBM and advanced memory allocation remain important, while customers optimize spend across platforms."),
            Sample("AVGO", "Broadcom Q1 2026 networking sample", "AI networking backlog and multi-year agreement demand remain constructive. Advanced packaging and interconnect capacity are monitored as infrastructure deployments scale."),
            Sample("MU", "Micron Q1 2026 HBM sample", "HBM3E and high bandwidth memory demand exceeds supply. DRAM pricing commentary remains constructive, allocation is tight, and long-term agreements support bit growth."),
            Sample("SNDK", "SanDisk Q1 2026 memory sample", "Advanced memory demand is improving with data center AI workloads. DRAM and HBM allocation signals remain important, though pricing pressure would be a slowdown warning."),
            Sample("ASML", "ASML Q1 2026 semicap sample", "Advanced packaging, capacity expansion, and AI infrastructure demand support customer investment. Any delay or normalization in leading-edge orders would be a warning signal."),
            Sample("TSM", "TSMC Q1 2026 packaging sample", "CoWoS and advanced packaging capacity remain tight. Chip-on-wafer-on-substrate demand exceeds supply, and capacity expansion continues for AI accelerators."),
            Sample("ANET", "Arista Q1 2026 networking sample", "Data center interconnect and AI networking demand remain strong. Backlog, allocation, and cloud customer capacity expansion support infrastructure momentum."),
            Sample("VRT", "Vertiv Q1 2026 power sample", "Power availability, grid constraint, substation timing, liquid cooling, and megawatt demand remain key data center bottlenecks as AI infrastructure expands."),
            Sample("SMCI", "Supermicro Q1 2026 AI server sample", "AI server backlog, GPU capacity, liquid cooling, and advanced memory allocation support demand. Inventory correction or lower demand would be slowdown warning language."),
            Sample("DELL", "Dell Q1 2026 AI server sample", "AI server demand includes GPU capacity, HBM, networking, and liquid cooling. Backlog and multi-year agreement commentary indicate infrastructure momentum."),
            Sample("MRVL", "Marvell Q1 2026 custom silicon sample", "Accelerated infrastructure, custom silicon, interconnect, and data center demand support AI revenue monetization. Customer delays would indicate moderation.")
        };

        var analyzer = new KeywordTranscriptAnalyzer();
        foreach (var sample in samples)
        {
            var company = companies.SingleOrDefault(x => x.Ticker == sample.Ticker);
            if (company is null)
            {
                continue;
            }

            var exists = await db.Transcripts.AnyAsync(x => x.CompanyId == company.Id && x.Title == sample.Title, cancellationToken);
            if (exists)
            {
                continue;
            }

            var transcript = new Transcript
            {
                CompanyId = company.Id,
                Ticker = company.Ticker,
                Market = company.ExchangeMarket,
                FiscalQuarterId = latest.Id,
                FiscalYear = latest.Year,
                FiscalQuarterNumber = latest.Quarter,
                PublishedDate = new DateOnly(2026, 4, 22),
                CallDate = new DateOnly(2026, 4, 22),
                Provider = "SampleSeed",
                Title = sample.Title,
                Text = sample.Text,
                RawText = sample.Text,
                SourceUrl = $"sample://transcripts/{company.Ticker}/2026/Q1",
                ImportedAtUtc = DateTimeOffset.UtcNow,
                ConfidenceScore = 50
            };
            db.Transcripts.Add(transcript);
            await db.SaveChangesAsync(cancellationToken);

            var sentiment = analyzer.ScoreDirectionalSignal(sample.Text);
            db.TranscriptMentions.AddRange(analyzer.Analyze(sample.Text).Select(x => new TranscriptMention
            {
                TranscriptId = transcript.Id,
                KeywordGroup = x.Group,
                Keyword = x.Keyword,
                Count = x.Count,
                SentimentScore = sentiment,
                ContextSnippet = sample.Text
            }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static SampleTranscript Sample(string ticker, string title, string text) => new(ticker, title, text);

    private sealed record SampleTranscript(string Ticker, string Title, string Text);

    private static async Task PurgeSampleDataAsync(AiCapexDbContext db, CancellationToken cancellationToken)
    {
        var sampleTranscripts = await db.Transcripts
            .Where(x =>
                x.Provider == "SampleSeed" ||
                (x.SourceUrl != null && x.SourceUrl.StartsWith("sample://")) ||
                x.Title.Contains(" sample"))
            .ToListAsync(cancellationToken);
        if (sampleTranscripts.Count > 0)
        {
            var transcriptIds = sampleTranscripts.Select(x => x.Id).ToList();
            db.TranscriptMentions.RemoveRange(db.TranscriptMentions.Where(x => transcriptIds.Contains(x.TranscriptId)));
            db.Transcripts.RemoveRange(sampleTranscripts);
        }

        db.FinancialMetrics.RemoveRange(db.FinancialMetrics.Where(x => x.Source == null));
        db.IndicatorSignals.RemoveRange(db.IndicatorSignals.Where(x => SeedSignalNames.Contains(x.Name)));
        db.RiskScoreSnapshots.RemoveRange(db.RiskScoreSnapshots);
        db.SourceDocuments.RemoveRange(db.SourceDocuments.Where(x =>
            x.Url.StartsWith("sample://") ||
            x.Url == "https://example.com/power-backlog" ||
            x.Title.Contains("Sample")));
        db.WatchlistAlerts.RemoveRange(db.WatchlistAlerts.Where(x => SeedAlertTitles.Contains(x.Title)));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SeedDataOptions
{
    public bool UseSampleData { get; set; } = true;
    public bool PurgeSampleDataWhenDisabled { get; set; } = true;
}
