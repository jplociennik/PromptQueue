namespace PromptQueue.Domain.Prompts;

/// <summary>Etapy cyklu życia promptu: oczekujący, przetwarzany oraz stany terminalne (zakończony / nieudany).</summary>
public enum PromptStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
