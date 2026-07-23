using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace PromptQueue.Worker;

/// <summary>Rejestracja przetwarzania: opcje (fail-fast), IChatClient (Ollama), procesor i hosted service.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPromptProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>()
            ?? throw new InvalidOperationException($"Configuration section '{WorkerOptions.SectionName}' is missing.");
        if (string.IsNullOrWhiteSpace(options.OllamaBaseUrl))
            throw new InvalidOperationException("Worker:OllamaBaseUrl is not configured.");
        if (string.IsNullOrWhiteSpace(options.OllamaModel))
            throw new InvalidOperationException("Worker:OllamaModel is not configured.");

        services.AddSingleton(options);
        services.AddSingleton<IChatClient>(_ => new OllamaApiClient(
            new HttpClient
            {
                BaseAddress = new Uri(options.OllamaBaseUrl),
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
            },
            options.OllamaModel));
        services.AddScoped<PromptProcessor>();
        services.AddHostedService<PromptProcessingWorker>();
        return services;
    }
}