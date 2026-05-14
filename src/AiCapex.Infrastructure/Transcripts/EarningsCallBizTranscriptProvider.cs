using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AiCapex.Application.Ingestion;
using AiCapex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCapex.Infrastructure.Transcripts;

public sealed partial class EarningsCallBizTranscriptProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<EarningsCallBizOptions> options,
    ILogger<EarningsCallBizTranscriptProvider> logger,
    AiCapexDbContext? db = null) : ITranscriptProvider
{
    private static readonly ConcurrentDictionary<string, TranscriptResult> StaleCache = new();
    private readonly EarningsCallBizOptions options = options.Value;

    public async Task<List<TranscriptMetadata>> GetTranscriptDatesAsync(string ticker, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || db is null)
        {
            return [];
        }

        var normalizedTicker = NormalizeTicker(ticker);
        var market = await ResolveMarketAsync(normalizedTicker, cancellationToken);
        if (market is null)
        {
            return [];
        }

        var currentYear = DateTime.UtcNow.Year + 1;
        var dates = new List<TranscriptMetadata>();
        for (var year = currentYear; year >= currentYear - 2; year--)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var result = await GetTranscriptAsync(market, normalizedTicker, year, quarter, cancellationToken);
                if (result is not null)
                {
                    dates.Add(new TranscriptMetadata(result.Ticker, year, quarter, result.CallDate, result.Provider, result.Title, result.SourceUrl));
                }
            }
        }

        return dates;
    }

    public async Task<TranscriptResult?> GetTranscriptAsync(string ticker, int year, int quarter, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || db is null)
        {
            return null;
        }

        var normalizedTicker = NormalizeTicker(ticker);
        var market = await ResolveMarketAsync(normalizedTicker, cancellationToken);
        return market is null ? null : await GetTranscriptAsync(market, normalizedTicker, year, quarter, cancellationToken);
    }

    public async Task<TranscriptResult?> GetTranscriptAsync(string market, string ticker, int year, int quarter, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return null;
        }

        market = NormalizeMarket(market);
        ticker = NormalizeTicker(ticker);
        ValidateYearAndQuarter(year, quarter);

        var cacheKey = BuildCacheKey(market, ticker, year, quarter);
        if (cache.TryGetValue<CachedTranscript>(cacheKey, out var cached) && cached is not null)
        {
            return cached.IsNotFound ? null : cached.Result;
        }

        if (options.RequestDelayMs > 0)
        {
            await Task.Delay(options.RequestDelayMs, cancellationToken);
        }

        var sourceUrl = BuildTranscriptUrl(market, ticker, year, quarter, options.BaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);

        using var response = await SendWithRetryAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            cache.Set(cacheKey, CachedTranscript.NotFound(), TimeSpan.FromHours(12));
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            logger.LogWarning("EarningsCallBiz returned 403 for {SourceUrl}", sourceUrl);
            return GetStale(cacheKey);
        }

        if ((int)response.StatusCode == 429)
        {
            logger.LogWarning("EarningsCallBiz rate limited request for {SourceUrl}", sourceUrl);
            return GetStale(cacheKey);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("EarningsCallBiz returned {StatusCode} for {SourceUrl}", response.StatusCode, sourceUrl);
            return GetStale(cacheKey);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = ParseHtml(html, ticker, year, quarter, sourceUrl);
        if (parsed is null)
        {
            cache.Set(cacheKey, CachedTranscript.NotFound(), TimeSpan.FromHours(12));
            return null;
        }

        parsed = parsed with { Market = market };
        if (options.CacheDays > 0)
        {
            cache.Set(cacheKey, CachedTranscript.Found(parsed), TimeSpan.FromDays(options.CacheDays));
        }

        StaleCache[cacheKey] = parsed;
        return parsed;
    }

    public static string BuildTranscriptUrl(string market, string ticker, int year, int quarter, string baseUrl = "https://earningscall.biz")
    {
        market = NormalizeMarket(market);
        ticker = NormalizeTicker(ticker).ToLowerInvariant();
        ValidateYearAndQuarter(year, quarter);
        return $"{baseUrl.TrimEnd('/')}/e/{market}/s/{ticker}/y/{year.ToString(CultureInfo.InvariantCulture)}/q/q{quarter.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static TranscriptResult? ParseHtml(string html, string ticker, int year, int quarter, string sourceUrl)
    {
        var title = ExtractTitle(html) ?? $"{ticker.ToUpperInvariant()} Q{quarter} {year} earnings call transcript";
        var contentHtml = PreferContentHtml(html);
        var text = CleanHtml(contentHtml);
        if (!LooksLikeTranscript(text))
        {
            return null;
        }

        var confidence = CalculateConfidence(title, ticker, year, quarter, text, contentHtml != html);
        if (text.Length < 2_000 && confidence < 30)
        {
            return null;
        }

        return new TranscriptResult(
            ticker.ToUpperInvariant(),
            year,
            quarter,
            ExtractCallDate(html),
            "EarningsCallBiz",
            title,
            text,
            sourceUrl,
            confidence);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            last = await httpClient.SendAsync(CloneRequest(request), cancellationToken);
            if ((int)last.StatusCode < 500)
            {
                return last;
            }

            if (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), cancellationToken);
            }
        }

        return last!;
    }

    private async Task<string?> ResolveMarketAsync(string ticker, CancellationToken cancellationToken)
    {
        var company = await db!.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Ticker == ticker.ToUpperInvariant(), cancellationToken);
        return string.IsNullOrWhiteSpace(company?.ExchangeMarket) ? null : NormalizeMarket(company.ExchangeMarket);
    }

    private static TranscriptResult? GetStale(string cacheKey) =>
        StaleCache.TryGetValue(cacheKey, out var stale) ? stale : null;

    private static string BuildCacheKey(string market, string ticker, int year, int quarter) =>
        $"earningscallbiz:{market}:{ticker.ToLowerInvariant()}:{year}:q{quarter}";

    private static string NormalizeMarket(string market) =>
        string.IsNullOrWhiteSpace(market) ? throw new ArgumentException("Market is required.", nameof(market)) : market.Trim().ToLowerInvariant();

    private static string NormalizeTicker(string ticker) =>
        string.IsNullOrWhiteSpace(ticker) ? throw new ArgumentException("Ticker is required.", nameof(ticker)) : ticker.Trim().ToUpperInvariant();

    private static void ValidateYearAndQuarter(int year, int quarter)
    {
        if (year < 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be 2000 or later.");
        }

        if (quarter is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(quarter), "Quarter must be between 1 and 4.");
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static string? ExtractTitle(string html)
    {
        var h1 = H1Regex().Match(html);
        if (h1.Success)
        {
            return CleanHtml(h1.Groups[1].Value).Trim();
        }

        var title = TitleRegex().Match(html);
        return title.Success ? CleanHtml(title.Groups[1].Value).Trim() : null;
    }

    private static DateOnly? ExtractCallDate(string html)
    {
        var time = TimeRegex().Match(html);
        if (time.Success && DateOnly.TryParse(time.Groups[1].Value, CultureInfo.InvariantCulture, out var date))
        {
            return date;
        }

        var text = CleanHtml(html);
        var match = DateRegex().Match(text);
        return match.Success && DateOnly.TryParse(match.Value, CultureInfo.InvariantCulture, out date) ? date : null;
    }

    private static string PreferContentHtml(string html)
    {
        foreach (var regex in new[] { ArticleRegex(), MainRegex() })
        {
            var match = regex.Match(html);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return html;
    }

    private static string CleanHtml(string html)
    {
        var withoutScripts = ScriptStyleRegex().Replace(html, "");
        var withoutChrome = ChromeRegex().Replace(withoutScripts, "");
        var withBreaks = BlockTagRegex().Replace(withoutChrome, "\n");
        var withoutTags = TagRegex().Replace(withBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalizedLines = decoded
            .Replace("\r", "\n")
            .Split('\n')
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .Where(line => !IsBoilerplate(line));

        return ExcessNewlineRegex().Replace(string.Join("\n", normalizedLines), "\n\n").Trim();
    }

    private static bool IsBoilerplate(string line) =>
        line.Equals("share buttons", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("download our app", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("privacy policy", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("terms of use", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTranscript(string text)
    {
        if (text.Length < 2_000)
        {
            return false;
        }

        var markers = new[] { "Operator", "Question", "Answer", "earnings", "conference call", "quarter" };
        return markers.Count(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)) >= 3;
    }

    private static int CalculateConfidence(string title, string ticker, int year, int quarter, string text, bool usedPreferredContainer)
    {
        var matchesTitle = title.Contains(ticker, StringComparison.OrdinalIgnoreCase) &&
            title.Contains(year.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) &&
            title.Contains($"Q{quarter}", StringComparison.OrdinalIgnoreCase);

        if (matchesTitle && text.Length > 10_000)
        {
            return 95;
        }

        if (text.Length > 5_000)
        {
            return 80;
        }

        return usedPreferredContainer ? 60 : 30;
    }

    private sealed record CachedTranscript(bool IsNotFound, TranscriptResult? Result)
    {
        public static CachedTranscript NotFound() => new(true, null);
        public static CachedTranscript Found(TranscriptResult result) => new(false, result);
    }

    [GeneratedRegex("<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex("<(?:nav|footer|aside|header)[\\s\\S]*?</(?:nav|footer|aside|header)>", RegexOptions.IgnoreCase)]
    private static partial Regex ChromeRegex();

    [GeneratedRegex("<(?:p|div|section|br|h1|h2|h3|li|tr|article|main|time)\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    [GeneratedRegex("[ \\t\\f\\v]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex ExcessNewlineRegex();

    [GeneratedRegex("<article\\b[^>]*>([\\s\\S]*?)</article>", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleRegex();

    [GeneratedRegex("<main\\b[^>]*>([\\s\\S]*?)</main>", RegexOptions.IgnoreCase)]
    private static partial Regex MainRegex();

    [GeneratedRegex("<h1\\b[^>]*>([\\s\\S]*?)</h1>", RegexOptions.IgnoreCase)]
    private static partial Regex H1Regex();

    [GeneratedRegex("<title\\b[^>]*>([\\s\\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<time\\b[^>]*datetime=[\"']([^\"']+)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex("\\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\\s+\\d{1,2},\\s+\\d{4}\\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();
}
