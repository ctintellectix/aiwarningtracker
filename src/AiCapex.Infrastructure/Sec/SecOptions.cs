namespace AiCapex.Infrastructure.Sec;

public sealed class SecOptions
{
    public string UserAgent { get; set; } = "AiCapexSlowdownMonitor/1.0 (contact@example.com)";
    public string CacheDirectory { get; set; } = "data/sec-cache";
}
