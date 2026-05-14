using Microsoft.Data.Sqlite;

if (args.Length < 2 || args[0] is not ("clear-imports" or "reverse-signal-scale"))
{
    Console.Error.WriteLine("Usage: AiCapex.DbTool <clear-imports|reverse-signal-scale> <sqlite-db-path>");
    return 2;
}

var dbPath = Path.GetFullPath(args[1]);
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 3;
}

var tablesToClear = new[]
{
    "TranscriptMentions",
    "Transcripts",
    "IndicatorSignals",
    "RiskScoreSnapshots",
    "WatchlistAlerts",
    "SourceDocuments",
    "FinancialMetrics",
    "CompanyFacts",
    "SecFilings",
    "FiscalQuarters"
};

await using var connection = new SqliteConnection($"Data Source={dbPath}");
await connection.OpenAsync();
await using var transaction = connection.BeginTransaction();

if (args[0] == "reverse-signal-scale")
{
    await Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS "ToolMetadata" (
            "Key" TEXT NOT NULL PRIMARY KEY,
            "Value" TEXT NOT NULL
        );
        """);
    if (await MetadataExists(connection, transaction, "signal-scale-reversed-v1"))
    {
        await transaction.CommitAsync();
        Console.WriteLine("Signal scale migration already applied.");
        return 0;
    }

    var indicatorRows = await TableExists(connection, transaction, "IndicatorSignals")
        ? await Execute(connection, transaction, """
            UPDATE "IndicatorSignals"
            SET "ScoreImpact" = -"ScoreImpact",
                "Direction" = CASE "Direction"
                    WHEN 0 THEN 2
                    WHEN 2 THEN 0
                    ELSE "Direction"
                END;
            """)
        : 0;
    var mentionRows = await TableExists(connection, transaction, "TranscriptMentions")
        ? await Execute(connection, transaction, """
            UPDATE "TranscriptMentions"
            SET "SentimentScore" = -"SentimentScore";
            """)
        : 0;
    await Execute(connection, transaction, "INSERT INTO \"ToolMetadata\" (\"Key\", \"Value\") VALUES ($key, $value);", ("$key", "signal-scale-reversed-v1"), ("$value", DateTimeOffset.UtcNow.ToString("O")));
    await transaction.CommitAsync();
    Console.WriteLine($"IndicatorSignals updated: {indicatorRows}");
    Console.WriteLine($"TranscriptMentions updated: {mentionRows}");
    return 0;
}

foreach (var table in tablesToClear)
{
    if (!await TableExists(connection, transaction, table))
    {
        continue;
    }

    await Execute(connection, transaction, $"DELETE FROM \"{table}\";");
}

if (await TableExists(connection, transaction, "sqlite_sequence"))
{
    foreach (var table in tablesToClear)
    {
        await Execute(connection, transaction, "DELETE FROM \"sqlite_sequence\" WHERE \"name\" = $table;", ("$table", (object)table));
    }
}

await transaction.CommitAsync();

foreach (var table in tablesToClear)
{
    if (!await TableExists(connection, null, table))
    {
        continue;
    }

    var count = await CountRows(connection, table);
    Console.WriteLine($"{table}: {count}");
}

if (await TableExists(connection, null, "Companies"))
{
    Console.WriteLine($"Companies: {await CountRows(connection, "Companies")}");
}

return 0;

static async Task<bool> TableExists(SqliteConnection connection, SqliteTransaction? transaction, string table)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table;";
    command.Parameters.AddWithValue("$table", table);
    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result) > 0;
}

static async Task<int> Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    return await command.ExecuteNonQueryAsync();
}

static async Task<bool> MetadataExists(SqliteConnection connection, SqliteTransaction transaction, string key)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT COUNT(*) FROM \"ToolMetadata\" WHERE \"Key\" = $key;";
    command.Parameters.AddWithValue("$key", key);
    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result) > 0;
}

static async Task<long> CountRows(SqliteConnection connection, string table)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt64(result);
}
