namespace AiCapex.Infrastructure.Sec;

public sealed record SecFactValue(
    string Taxonomy,
    string Tag,
    string Unit,
    int FiscalYear,
    string FiscalPeriod,
    string Form,
    DateOnly? FiledDate,
    DateOnly? EndDate,
    decimal Value,
    string SourceUrl,
    string? AccessionNumber = null,
    DateOnly? StartDate = null);

public sealed record SecExtractedMetric(
    string MetricName,
    int FiscalYear,
    int FiscalQuarter,
    DateOnly? PeriodEndDate,
    decimal Value,
    string Unit,
    string SourceUrl);
