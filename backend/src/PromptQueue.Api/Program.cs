using Microsoft.Extensions.Configuration;
using PromptQueue.Api;
using PromptQueue.Infrastructure;
using PromptQueue.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Wspólny appsettings.json (z backend/, linkowany do output) — ładowany z katalogu aplikacji,
// bo przy `dotnet run` content root to katalog projektu, gdzie tego pliku nie ma.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

await app.Services.ApplyMigrationsAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
