using PromptQueue.Api;
using PromptQueue.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

await app.Services.ApplyMigrationsAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
