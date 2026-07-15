using PromptQueue.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<PromptProcessingWorker>();

var host = builder.Build();
host.Run();
