using Microsoft.Extensions.Configuration;
using PromptQueue.Infrastructure;
using PromptQueue.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPromptProcessing(builder.Configuration);

var host = builder.Build();
host.Run();
