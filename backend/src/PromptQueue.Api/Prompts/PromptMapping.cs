using PromptQueue.Domain.Prompts;

namespace PromptQueue.Api.Prompts;

/// <summary>Projekcja encji domenowej Prompt na kontrakt API (PromptResponse).</summary>
public static class PromptMapping
{
    /// <summary>Mapuje encję promptu na odpowiedź API (stan, wynik/błąd, znaczniki czasu).</summary>
    public static PromptResponse ToResponse(this Prompt prompt) => new(
        prompt.Id, prompt.Content, prompt.Status,
        prompt.Result, prompt.ErrorMessage, prompt.CreatedAt, prompt.UpdatedAt);
}
