namespace PromptQueue.Worker;

/// <summary>Konfiguracja workera: endpoint i model Ollamy oraz parametry pętli. SSOT ustawień przetwarzania.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    public string OllamaBaseUrl { get; init; } = "";
    public string OllamaModel { get; init; } = "";
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 10;
    public int RequestTimeoutSeconds { get; init; } = 120;
    public int RetryDelaySeconds { get; init; } = 3;
}
