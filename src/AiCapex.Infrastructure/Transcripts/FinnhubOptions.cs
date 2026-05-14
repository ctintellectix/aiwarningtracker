namespace AiCapex.Infrastructure.Transcripts;

public sealed class FinnhubOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://finnhub.io/api/v1";
}
