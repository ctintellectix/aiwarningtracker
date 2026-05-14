namespace AiCapex.Infrastructure.Transcripts;

public sealed class EarningsCallBizOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://earningscall.biz";
    public string UserAgent { get; set; } = "AiCapexMonitor/1.0 contact@example.com";
    public int CacheDays { get; set; } = 7;
    public int RequestDelayMs { get; set; } = 1000;
}
