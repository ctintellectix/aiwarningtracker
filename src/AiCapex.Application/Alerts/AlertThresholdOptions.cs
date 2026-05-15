namespace AiCapex.Application.Alerts;

public sealed class AlertThresholdOptions
{
    public static AlertThresholdOptions Default { get; } = new();

    public int ScoreDeteriorationPoints { get; init; } = 10;
    public decimal CapexOcfStressPercent { get; init; } = 75m;
    public decimal CategoryWeakeningSignal { get; init; } = -3m;
}
