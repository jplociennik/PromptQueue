using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using PromptQueue.Api;
using PromptQueue.Api.Prompts;
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

// Enum jako string camelCase w JSON (np. "pending") — wprost mapowalny na StatusBadge frontu (pq-5).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

app.UseExceptionHandler();

await app.Services.ApplyMigrationsAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapPromptEndpoints();

app.Run();

// Udostępnia klasę wejściową testom integracyjnym (WebApplicationFactory<Program>).
public partial class Program;
