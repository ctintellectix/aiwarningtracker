using System.Globalization;
using System.Text.Json;
using AiCapex.Application.Ingestion;
using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure.Transcripts;

public sealed class FinnhubTranscriptProvider(HttpClient httpClient, IOptions<FinnhubOptions> options) : ITranscriptProvider
{
    private readonly FinnhubOptions options = options.Value;

    public async Task<List<TranscriptMetadata>> GetTranscriptDatesAsync(string ticker, CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return [];
        }

        try
        {
            var listUrl = BuildListUrl(ticker);
            using var response = await httpClient.GetAsync(listUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseTranscriptList(json, ticker, options.BaseUrl.TrimEnd('/'))
                .Select(x => new TranscriptMetadata(ticker, x.Year, x.Quarter, x.Date, "Finnhub", x.Title, x.SourceUrl))
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
            var listUrl = BuildListUrl(ticker);
            using var listResponse = await httpClient.GetAsync(listUrl, cancellationToken);
            if (!listResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken);
            var match = ParseTranscriptList(listJson, ticker, options.BaseUrl.TrimEnd('/'))
                .FirstOrDefault(x => x.Year == year && x.Quarter == quarter);
            if (match is null)
            {
                return null;
            }

            var transcriptUrl = BuildTranscriptUrl(match.Id);
            using var transcriptResponse = await httpClient.GetAsync(transcriptUrl, cancellationToken);
            if (!transcriptResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var transcriptJson = await transcriptResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseTranscript(transcriptJson, ticker, match, transcriptUrl);
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<FinnhubTranscriptListItem> ParseTranscriptList(string json, string ticker, string baseUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("transcripts", out var transcripts)
            ? transcripts
            : root;
        if (list.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in list.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var year = ReadInt(item, "year");
            var quarter = ReadInt(item, "quarter");
            if (string.IsNullOrWhiteSpace(id) || year is null || quarter is null)
            {
                continue;
            }

            var title = ReadString(item, "title") ?? $"{ticker.ToUpperInvariant()} Q{quarter} {year} earnings call";
            var date = ReadDate(item, "time") ?? ReadDate(item, "date");
            var sourceUrl = $"{baseUrl}/stock/transcripts?id={Uri.EscapeDataString(id)}";
            yield return new FinnhubTranscriptListItem(id, year.Value, quarter.Value, date, title, sourceUrl);
        }
    }

    public static TranscriptResult? ParseTranscript(string json, string ticker, FinnhubTranscriptListItem listItem, string sourceUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var year = ReadInt(root, "year") ?? listItem.Year;
        var quarter = ReadInt(root, "quarter") ?? listItem.Quarter;
        var symbol = ReadString(root, "symbol") ?? ticker;
        var title = ReadString(root, "title") ?? listItem.Title;
        var callDate = ReadDate(root, "time") ?? ReadDate(root, "date") ?? listItem.Date;
        var text = ReadTranscriptText(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new TranscriptResult(symbol.ToUpperInvariant(), year, quarter, callDate, "Finnhub", title, text, sourceUrl, 85);
    }

    private string BuildListUrl(string ticker) =>
        $"{options.BaseUrl.TrimEnd('/')}/stock/transcripts/list?symbol={Uri.EscapeDataString(ticker)}&token={Uri.EscapeDataString(options.ApiKey ?? "")}";

    private string BuildTranscriptUrl(string id) =>
        $"{options.BaseUrl.TrimEnd('/')}/stock/transcripts?id={Uri.EscapeDataString(id)}&token={Uri.EscapeDataString(options.ApiKey ?? "")}";

    private static string ReadTranscriptText(JsonElement root)
    {
        if (root.TryGetProperty("transcript", out var transcript))
        {
            if (transcript.ValueKind == JsonValueKind.String)
            {
                return transcript.GetString() ?? "";
            }

            if (transcript.ValueKind == JsonValueKind.Array)
            {
                var parts = transcript
                    .EnumerateArray()
                    .Select(segment =>
                    {
                        var speaker = ReadString(segment, "name") ?? ReadString(segment, "speaker") ?? "";
                        var speech = ReadString(segment, "speech") ?? ReadString(segment, "text") ?? "";
                        return string.IsNullOrWhiteSpace(speaker) ? speech : $"{speaker}: {speech}";
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                return string.Join(Environment.NewLine, parts);
            }
        }

        return ReadString(root, "content") ?? ReadString(root, "text") ?? "";
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

public sealed record FinnhubTranscriptListItem(string Id, int Year, int Quarter, DateOnly? Date, string Title, string SourceUrl);
