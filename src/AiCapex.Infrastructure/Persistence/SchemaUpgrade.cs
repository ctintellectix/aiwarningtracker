using Microsoft.EntityFrameworkCore;

namespace AiCapex.Infrastructure.Persistence;

public static class SchemaUpgrade
{
    public static async Task EnsureCompatibleSchemaAsync(AiCapexDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        await AddColumnIfMissing(db, "Companies", "Cik", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Companies", "Sector", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Companies", "Industry", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Companies", "ExchangeMarket", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Companies", "IsHyperscaler", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissing(db, "Companies", "IsSemiconductor", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissing(db, "Companies", "IsDataCenterInfrastructure", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        await AddColumnIfMissing(db, "FinancialMetrics", "FiscalYear", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "FinancialMetrics", "FiscalQuarterNumber", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "FinancialMetrics", "PeriodEndDate", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "FinancialMetrics", "SourcePeriodLabel", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "FinancialMetrics", "MetricName", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "FinancialMetrics", "Source", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "FinancialMetrics", "SourceUrl", "TEXT NULL", cancellationToken);

        await AddColumnIfMissing(db, "SourceDocuments", "Provider", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "PublishedAtUtc", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "RetrievedAtUtc", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "RawText", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "Snippet", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "CredibilityWeight", "TEXT NOT NULL DEFAULT '1.0'", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "Status", "TEXT NOT NULL DEFAULT 'Imported'", cancellationToken);

        await AddColumnIfMissing(db, "Transcripts", "Ticker", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "Market", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "FiscalYear", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "FiscalQuarterNumber", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "PeriodEndDate", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "SourcePeriodLabel", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "CallDate", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "Provider", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "RawText", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "SourceUrl", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "ImportedAtUtc", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "Transcripts", "ConfidenceScore", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        await AddColumnIfMissing(db, "TranscriptMentions", "SentimentScore", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await AddColumnIfMissing(db, "TranscriptMentions", "ContextSnippet", "TEXT NULL", cancellationToken);

        await AddColumnIfMissing(db, "IndicatorSignals", "SignalDate", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "IndicatorSignals", "SignalName", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "IndicatorSignals", "Strength", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissing(db, "IndicatorSignals", "Confidence", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissing(db, "IndicatorSignals", "SourceDocumentId", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "IndicatorSignals", "Explanation", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "AnalysisProvider", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "AnalysisModel", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "SourceDocuments", "AnalysisJson", "TEXT NULL", cancellationToken);

        await AddColumnIfMissing(db, "RiskScoreSnapshots", "SnapshotDate", "TEXT NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "OverallScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "HyperscalerCapexScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "HbmDramScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "CowosPackagingScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "DataCenterPowerScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "AiRevenueScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "FinancialStressScore", "INTEGER NULL", cancellationToken);
        await AddColumnIfMissing(db, "RiskScoreSnapshots", "ExplanationJson", "TEXT NULL", cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SecFilings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SecFilings" PRIMARY KEY AUTOINCREMENT,
                "CompanyId" INTEGER NOT NULL,
                "AccessionNumber" TEXT NOT NULL,
                "FilingType" TEXT NOT NULL,
                "FilingDate" TEXT NULL,
                "PeriodEndDate" TEXT NULL,
                "SecUrl" TEXT NOT NULL,
                "RawJsonPath" TEXT NULL,
                "ParsedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_SecFilings_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE CASCADE
            );
            """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CompanyFacts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CompanyFacts" PRIMARY KEY AUTOINCREMENT,
                "CompanyId" INTEGER NOT NULL,
                "Taxonomy" TEXT NOT NULL,
                "Tag" TEXT NOT NULL,
                "Unit" TEXT NOT NULL,
                "FiscalYear" INTEGER NOT NULL,
                "FiscalPeriod" TEXT NOT NULL,
                "Form" TEXT NOT NULL,
                "FiledDate" TEXT NULL,
                "EndDate" TEXT NULL,
                "Value" TEXT NOT NULL,
                "SourceUrl" TEXT NOT NULL,
                CONSTRAINT "FK_CompanyFacts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE CASCADE
            );
            """, cancellationToken);

    }

    private static async Task AddColumnIfMissing(AiCapexDbContext db, string table, string column, string definition, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)});";

        var existing = new List<string>();
        await using (var reader = await columnCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (!existing.Contains(column, StringComparer.OrdinalIgnoreCase))
        {
            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {QuoteIdentifier(table)} ADD COLUMN {QuoteIdentifier(column)} {definition};";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
