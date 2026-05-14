namespace AiCapex.Application.Dashboard;

public static class BulkImportSummary
{
    public static BulkImportResultDto Create(string source, IReadOnlyList<BulkImportItemDto> results) =>
        new(
            source,
            results.Count,
            results.Count(x => x.Success),
            results.Count(x => !x.Success),
            results.Sum(x => x.DocumentsImported),
            results.Sum(x => x.SignalsImported),
            results);
}
