using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.Persistence;
using AiCapex.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Services;

public class AiCapexDataServiceTests
{
    [Fact]
    public async Task GetCompanyAsync_returns_company_detail_without_projection_filter_translation_error()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await SeedData.EnsureSeededAsync(db);

        var company = await new AiCapexDataService(db).GetCompanyAsync("MU");

        Assert.NotNull(company);
        Assert.Equal("MU", company.Company.Ticker);
        Assert.Contains(company.Signals, signal => signal.Category.ToString() == "HbmDramPricingAllocation");
    }

    [Fact]
    public async Task GetAlertsAsync_orders_alerts_without_sqlite_datetimeoffset_translation_error()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.WatchlistAlerts.AddRange(
            new WatchlistAlert
            {
                Severity = AlertSeverity.Warning,
                Title = "Older alert",
                Message = "First",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            },
            new WatchlistAlert
            {
                Severity = AlertSeverity.Critical,
                Title = "Newer alert",
                Message = "Second",
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var alerts = await new AiCapexDataService(db).GetAlertsAsync();

        Assert.Equal(["Newer alert", "Older alert"], alerts.Select(x => x.Title).ToArray());
    }
}
