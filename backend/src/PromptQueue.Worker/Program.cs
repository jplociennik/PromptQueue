using Microsoft.Extensions.Configuration;
using PromptQueue.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Wspólny appsettings.json (z backend/, linkowany do output) — patrz Api/Program.cs.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);

builder.Services.AddHostedService<PromptProcessingWorker>();

var host = builder.Build();
host.Run();
