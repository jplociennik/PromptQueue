namespace PromptQueue.Api.Prompts;

/// <summary>Walidacja batch-POST: rozmiar listy i pojedynczego promptu (długość po trymowaniu). SSOT limitów żądania.</summary>
public static class CreatePromptsRequestValidator
{
    public const int MaxPromptsPerRequest = 50;
    public const int MaxPromptLength = 8_000;

    /// <summary>Zwraca słownik błędów walidacji (pusty = żądanie poprawne); długość liczona po trymowaniu.</summary>
    public static Dictionary<string, string[]> Validate(CreatePromptsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Prompts is null || request.Prompts.Count == 0)
        {
            errors["prompts"] = ["At least one prompt is required."];
            return errors;
        }

        if (request.Prompts.Count > MaxPromptsPerRequest)
            errors["prompts"] = [$"A single request accepts at most {MaxPromptsPerRequest} prompts."];

        for (var i = 0; i < request.Prompts.Count; i++)
        {
            var content = request.Prompts[i];
            if (string.IsNullOrWhiteSpace(content))
                errors[$"prompts[{i}]"] = ["Prompt must not be empty."];
            else if (content.Trim().Length > MaxPromptLength)
                errors[$"prompts[{i}]"] = [$"Prompt must not exceed {MaxPromptLength} characters."];
        }

        return errors;
    }
}
