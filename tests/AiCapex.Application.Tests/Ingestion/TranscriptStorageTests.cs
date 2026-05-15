using AiCapex.Application.Analysis;
using AiCapex.Application.Ingestion;
using AiCapex.Domain.Entities;
using AiCapex.Domain.Scoring;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Transcripts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Ingestion;

public class TranscriptStorageTests
{
    [Fact]
    public async Task Keeps_soft_ai_signal_neutral_when_assigning_direction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Companies.Add(new Company { Ticker = "MU", Name = "Micron", Segment = "Memory/HBM" });
        await db.SaveChangesAsync();

        await TranscriptStorage.StoreAsync(
            db,
            new SoftPositiveNarrativeAnalysisService(),
            new TranscriptResult(
                "MU",
                2026,
                2,
                new DateOnly(2026, 3, 31),
                "Test",
                "Micron call",
                "Operator. Micron reported HBM demand exceeds supply during the quarter.",
                "https://example.com/mu",
                90),
            CancellationToken.None);

        var signal = await db.IndicatorSignals.SingleAsync();
        Assert.Equal(SignalDirection.Neutral, signal.Direction);
        Assert.Empty(await db.TranscriptMentions.ToListAsync());
    }

    private sealed class SoftPositiveNarrativeAnalysisService : IDocumentNarrativeAnalysisService
    {
        public Task<DocumentNarrativeAnalysisResult> AnalyzeAsync(DocumentNarrativeAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentNarrativeAnalysisResult(
                "OpenAI",
                "test-model",
                false,
                "Narrative summary.",
                90,
                [new DocumentCategorySignal(RiskScoreCategory.FinancialStressFreeCashFlow, 0.73m, "Constructive but measured.", "Positive cash flow evidence.", 88)]));
    }
}
