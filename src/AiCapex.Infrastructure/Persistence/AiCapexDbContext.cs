using AiCapex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Persistence;

public sealed class AiCapexDbContext(DbContextOptions<AiCapexDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<FiscalQuarter> FiscalQuarters => Set<FiscalQuarter>();
    public DbSet<FinancialMetric> FinancialMetrics => Set<FinancialMetric>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<TranscriptMention> TranscriptMentions => Set<TranscriptMention>();
    public DbSet<IndicatorSignal> IndicatorSignals => Set<IndicatorSignal>();
    public DbSet<RiskScoreSnapshot> RiskScoreSnapshots => Set<RiskScoreSnapshot>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<WatchlistAlert> WatchlistAlerts => Set<WatchlistAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>().HasIndex(x => x.Ticker).IsUnique();
        modelBuilder.Entity<FiscalQuarter>().HasIndex(x => new { x.Year, x.Quarter }).IsUnique();
        modelBuilder.Entity<FinancialMetric>().Property(x => x.Kind).HasConversion<string>();
        modelBuilder.Entity<IndicatorSignal>().Property(x => x.Category).HasConversion<string>();
        modelBuilder.Entity<IndicatorSignal>().Property(x => x.Direction).HasConversion<string>();
        modelBuilder.Entity<SourceDocument>().Property(x => x.SourceType).HasConversion<string>();
        modelBuilder.Entity<WatchlistAlert>().Property(x => x.Severity).HasConversion<string>();
    }
}
