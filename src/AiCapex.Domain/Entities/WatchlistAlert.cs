namespace AiCapex.Domain.Entities;

public sealed class WatchlistAlert
{
    public int Id { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsAcknowledged { get; set; }
}
