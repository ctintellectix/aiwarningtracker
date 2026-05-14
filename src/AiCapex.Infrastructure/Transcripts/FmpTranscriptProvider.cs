using System.Globalization;
using System.Text.Json;
using AiCapex.Application.Ingestion;
using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure.Transcripts;

public sealed class FmpTranscriptProvider(HttpClient httpClient, IOptions<FmpOptions> options) : ITranscriptProvider
{
    private readonly FmpOptions options = options.Value;

    public async Task<List<TranscriptMetadata>> GetTranscriptDatesAsync(string ticker, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return [];
        }

        try
        {
            var dateUrl = $"{options.BaseUrl.TrimEnd('/')}/earning-call-transcript-dates?symbol={Uri.EscapeDataString(ticker)}&apikey={Uri.EscapeDataString(options.ApiKey)}";
            using var dateResponse = await httpClient.GetAsync(dateUrl, cancellationToken);
            if (!dateResponse.IsSuccessStatusCode)
            {
                return [];
            }

            var dateJson = await dateResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseDates(dateJson)
                .Select(x => new TranscriptMetadata(ticker, x.Year, x.Quarter, x.Date, "FMP", $"{ticker} Q{x.Quarter} {x.Year} earnings call", null))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return null;
        }

        try
        {
            var transcriptUrl = $"{options.BaseUrl.TrimEnd('/')}/earning-call-transcript?symbol={Uri.EscapeDataString(ticker)}&year={year.ToString(CultureInfo.InvariantCulture)}&quarter={quarter.ToString(CultureInfo.InvariantCulture)}&apikey={Uri.EscapeDataString(options.ApiKey)}";
            using var transcriptResponse = await httpClient.GetAsync(transcriptUrl, cancellationToken);
            if (!transcriptResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var transcriptJson = await transcriptResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseTranscripts(transcriptJson, ticker, transcriptUrl).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    internal static IEnumerable<(int Year, int Quarter, DateOnly? Date)> ParseDates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var year = ReadInt(item, "year");
            var quarter = ReadInt(item, "quarter");
            if (year is null || quarter is null)
            {
                continue;
            }

            yield return (year.Value, quarter.Value, ReadDate(item, "date"));
        }
    }

    internal static IEnumerable<TranscriptResult> ParseTranscripts(string json, string ticker, string sourceUrl)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var year = ReadInt(item, "year") ?? 0;
            var quarter = ReadInt(item, "quarter") ?? 0;
            var content = ReadString(item, "content") ?? ReadString(item, "transcript") ?? "";
            if (year <= 0 || quarter <= 0 || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var symbol = ReadString(item, "symbol") ?? ticker;
            var date = ReadDate(item, "date");
            yield return new TranscriptResult(symbol.ToUpperInvariant(), year, quarter, date, "FMP", $"{symbol.ToUpperInvariant()} Q{quarter} {year} earnings call", content, sourceUrl, 85);
        }
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateOnly? ReadDate(JsonElement element, string property)
    {
        var raw = ReadString(element, property);
        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var date) ? date : null;
    }
}
