using PromptQueue.Domain.Prompts;

namespace PromptQueue.TestSupport;

/// <summary>Buduje encję Prompt w zadanym stanie cyklu życia (przez metody przejść) do seedowania testów.</summary>
public sealed class PromptBuilder
{
    private string _content = "Przetłumacz ten akapit na język francuski.";

    public PromptBuilder WithContent(string content)
    {
        _content = content;
        return this;
    }

    /// <summary>Prompt świeżo utworzony (Pending).</summary>
    public Prompt Pending() => new(_content);

    /// <summary>Prompt przejęty do przetwarzania (Processing).</summary>
    public Prompt Processing()
    {
        var prompt = Pending();
        prompt.StartProcessing();
        return prompt;
    }

    /// <summary>Prompt zakończony sukcesem (Completed) z wynikiem.</summary>
    public Prompt Completed(string result = "Wynik przetwarzania.")
    {
        var prompt = Processing();
        prompt.Complete(result);
        return prompt;
    }

    /// <summary>Prompt zakończony błędem (Failed) z komunikatem.</summary>
    public Prompt Failed(string error = "Błąd przetwarzania.")
    {
        var prompt = Pending();
        prompt.Fail(error);
        return prompt;
    }
}
