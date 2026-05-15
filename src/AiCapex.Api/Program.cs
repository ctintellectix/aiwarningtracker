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
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:4173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();
var pathBase = builder.Configuration["Deployment:PathBase"];

if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase);
}

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
