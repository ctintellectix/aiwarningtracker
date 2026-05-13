using AiCapex.Domain.Entities;
using AiCapex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiCapex.Application.Tests.Persistence;

public class SeedDataTests
{
    [Fact]
    public async Task EnsureSeeded_includes_sndk_as_memory_hbm_company()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);

        await SeedData.EnsureSeededAsync(db);

        var company = await db.Companies.SingleAsync(x => x.Ticker == "SNDK");
        Assert.Equal("SanDisk", company.Name);
        Assert.Equal("Memory/HBM", company.Segment);
    }

    [Fact]
    public async Task EnsureSeeded_adds_missing_sndk_to_existing_seeded_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AiCapexDbContext>().UseSqlite(connection).Options;
        await using var db = new AiCapexDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Companies.Add(new Company { Ticker = "MSFT", Name = "Microsoft", Segment = "Hyperscaler" });
        await db.SaveChangesAsync();

        await SeedData.EnsureSeededAsync(db);

        var company = await db.Companies.SingleAsync(x => x.Ticker == "SNDK");
        Assert.Equal("Memory/HBM", company.Segment);
    }
}
