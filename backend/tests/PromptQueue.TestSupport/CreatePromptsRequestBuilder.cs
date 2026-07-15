using PromptQueue.Api.Prompts;

namespace PromptQueue.TestSupport;

/// <summary>Buduje CreatePromptsRequest dla testów; sensowne domyślne + warianty brzegowe.</summary>
public sealed class CreatePromptsRequestBuilder
{
    private readonly List<string> _prompts = ["Przetłumacz ten akapit na język francuski."];

    public CreatePromptsRequestBuilder WithPrompts(params string[] prompts)
    {
        _prompts.Clear();
        _prompts.AddRange(prompts);
        return this;
    }

    public CreatePromptsRequestBuilder WithNoPrompts()
    {
        _prompts.Clear();
        return this;
    }

    public CreatePromptsRequestBuilder WithPromptCount(int count)
    {
        _prompts.Clear();
        _prompts.AddRange(Enumerable.Range(1, count).Select(i => $"Prompt {i}"));
        return this;
    }

    public CreatePromptsRequestBuilder WithOversizedPrompt()
    {
        _prompts.Clear();
        _prompts.Add(new string('x', CreatePromptsRequestValidator.MaxPromptLength + 1));
        return this;
    }

    public CreatePromptsRequest Build() => new(_prompts);
}
