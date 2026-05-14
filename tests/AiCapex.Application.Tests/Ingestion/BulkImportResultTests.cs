using AiCapex.Application.Dashboard;

namespace AiCapex.Application.Tests.Ingestion;

public class BulkImportResultTests
{
    [Fact]
    public void Summarizes_successes_failures_documents_and_signals()
    {
        var items = new[]
        {
            new BulkImportItemDto("MSFT", true, "ok", 2, 1),
            new BulkImportItemDto("ASML", false, "No CIK", 0, 0)
        };

        var result = BulkImportSummary.Create("SEC EDGAR", items);

        Assert.Equal(2, result.CompaniesProcessed);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(2, result.DocumentsImported);
        Assert.Equal(1, result.SignalsImported);
    }
}
