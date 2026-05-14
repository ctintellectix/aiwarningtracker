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
    CapexAsPercentOfOperatingCashFlow,
    Revenue,
    Debt,
    TtmCapex,
    CapexQoqGrowth,
    CapexYoyGrowth
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
    SecFiling,
    Transcript,
    ManualEntry,
    NewsRss
}
