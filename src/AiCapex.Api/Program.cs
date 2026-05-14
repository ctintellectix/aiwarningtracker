using AiCapex.Application.Alerts;
using AiCapex.Application.Scoring;
using AiCapex.Infrastructure;
using AiCapex.Infrastructure.Persistence;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:4173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AiCapexDbContext>();
    var seedOptions = builder.Configuration.GetSection("SeedData").Get<SeedDataOptions>() ?? new SeedDataOptions();
    await SeedData.EnsureSeededAsync(db, seedOptions);

    if (!seedOptions.UseSampleData)
    {
        var scoringService = scope.ServiceProvider.GetRequiredService<IRiskScoringService>();
        await scoringService.RecalculateAsync();
        var alertGenerationService = scope.ServiceProvider.GetRequiredService<IAlertGenerationService>();
        await alertGenerationService.GenerateAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");
app.MapControllers();
app.Run();
