namespace AiCapex.Domain.Entities;

public enum GuidanceDirection
{
    Raise,
    Hold,
    Cut
}

public enum MetricKind
{
    QuarterlyCapex,
    OperatingCashFlow,
    FreeCashFlowEstimate,
    CapexAsPercentOfOperatingCashFlow
}

public enum SignalDirection
{
    Bullish,
    Neutral,
    Bearish
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum SourceType
{
    SecXbrl,
    Transcript,
    ManualEntry,
    NewsRss
}
