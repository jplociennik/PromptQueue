using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using PromptQueue.Api;
using PromptQueue.Api.Prompts;
using PromptQueue.Infrastructure;
using PromptQueue.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddCors();

// Enum jako string camelCase w JSON (np. "pending") — wprost mapowalny na StatusBadge frontu (pq-5).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

app.UseExceptionHandler();

// Dev-only CORS: wygoda wołania Api z originu frontu w developmencie (obok Vite dev-proxy). Prod-CORS → pq-6.
if (app.Environment.IsDevelopment())
    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

await app.Services.ApplyMigrationsAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapPromptEndpoints();

app.Run();

// Udostępnia klasę wejściową testom integracyjnym (WebApplicationFactory<Program>).
public partial class Program;
