namespace AiCapex.Domain.Entities;

public sealed class FiscalQuarter
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Quarter { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Label => $"Q{Quarter} {Year}";
}
