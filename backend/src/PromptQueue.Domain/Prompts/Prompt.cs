namespace PromptQueue.Domain.Prompts;

/// <summary>Zgłoszony prompt i jego cykl życia; SSOT reguł przejść stanu i własnego Id.</summary>
public class Prompt
{
    // Konstruktor bezparametrowy dla EF Core (materializacja z bazy); prywatny, bo w kodzie
    // domenowym prompt powstaje wyłącznie przez konstruktor publiczny nadający Id i stan.
    private Prompt() { }

    public Prompt(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));

        Id = Guid.NewGuid();
        Content = content;
        Status = PromptStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public string Content { get; private set; } = null!;
    public PromptStatus Status { get; private set; }
    public string? Result { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Rozpoczyna przetwarzanie; dozwolone wyłącznie ze stanu oczekującego.</summary>
    public void StartProcessing()
    {
        EnsureStatus(PromptStatus.Pending);
        Status = PromptStatus.Processing;
        Touch();
    }

    /// <summary>Kończy prompt sukcesem z wynikiem; dozwolone wyłącznie z przetwarzania.</summary>
    public void Complete(string result)
    {
        EnsureStatus(PromptStatus.Processing);
        Result = result;
        Status = PromptStatus.Completed;
        Touch();
    }

    /// <summary>Oznacza prompt jako nieudany; dozwolone z każdego stanu poza terminalnym.</summary>
    public void Fail(string error)
    {
        EnsureNotTerminal();
        ErrorMessage = error;
        Status = PromptStatus.Failed;
        Touch();
    }

    /// <summary>Zwraca prompt do kolejki (np. po przerwaniu przetwarzania); dozwolone wyłącznie z Processing.</summary>
    public void Requeue()
    {
        EnsureStatus(PromptStatus.Processing);
        Status = PromptStatus.Pending;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private void EnsureStatus(PromptStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Expected {expected} but was {Status}.");
    }

    private void EnsureNotTerminal()
    {
        if (Status is PromptStatus.Completed or PromptStatus.Failed)
            throw new InvalidOperationException($"Prompt is in terminal state {Status}.");
    }
}
