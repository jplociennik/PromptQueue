using PromptQueue.Domain.Prompts;

namespace PromptQueue.Api.Prompts;

/// <summary>Żądanie wsadowego dodania promptów: lista treści do zakolejkowania.</summary>
public record CreatePromptsRequest(IReadOnlyList<string> Prompts);

/// <summary>Odpowiedź na dodanie wsadu: identyfikatory utworzonych promptów i ich wspólny stan początkowy.</summary>
public record CreatePromptsResponse(IReadOnlyList<Guid> Ids, PromptStatus Status);

/// <summary>Reprezentacja promptu w API: stan oraz wynik/błąd, bez wycieku encji domenowej na zewnątrz.</summary>
public record PromptResponse(
    Guid Id,
    string Content,
    PromptStatus Status,
    string? Result,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime UpdatedAt);
