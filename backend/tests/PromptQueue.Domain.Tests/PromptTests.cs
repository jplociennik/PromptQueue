using PromptQueue.Domain.Prompts;

namespace PromptQueue.Domain.Tests;

/// <summary>Testy jednostkowe cyklu życia promptu: stan początkowy, dozwolone przejścia i bramki nielegalnych zmian.</summary>
public class PromptTests
{
    private const string SampleContent = "Przetłumacz ten akapit na język francuski.";
    private const string SampleResult = "Bonjour le monde.";
    private const string SampleError = "Model niedostępny.";

    private static Prompt PendingPrompt() => new(SampleContent);

    private static Prompt ProcessingPrompt()
    {
        var prompt = PendingPrompt();
        prompt.StartProcessing();
        return prompt;
    }

    private static Prompt CompletedPrompt()
    {
        var prompt = ProcessingPrompt();
        prompt.Complete(SampleResult);
        return prompt;
    }

    private static Prompt FailedPrompt()
    {
        var prompt = PendingPrompt();
        prompt.Fail(SampleError);
        return prompt;
    }

    // --- Konstruktor ---

    [Fact]
    public void Should_StartAsPending_WhenCreated()
    {
        var prompt = new Prompt(SampleContent);

        Assert.Equal(PromptStatus.Pending, prompt.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ThrowArgumentException_WhenContentIsBlank(string? content)
    {
        Assert.Throws<ArgumentException>(() => new Prompt(content!));
    }

    // --- StartProcessing ---

    [Fact]
    public void Should_TransitionToProcessing_WhenStartingFromPending()
    {
        var prompt = PendingPrompt();

        prompt.StartProcessing();

        Assert.Equal(PromptStatus.Processing, prompt.Status);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenStartingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.StartProcessing());
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenStartingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.StartProcessing());
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenStartingFromFailed()
    {
        var prompt = FailedPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.StartProcessing());
    }

    // --- Complete ---

    [Fact]
    public void Should_SetResultAndComplete_WhenCompletingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        prompt.Complete(SampleResult);

        Assert.Equal(SampleResult, prompt.Result);
        Assert.Equal(PromptStatus.Completed, prompt.Status);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenCompletingFromPending()
    {
        var prompt = PendingPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.Complete(SampleResult));
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenCompletingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.Complete(SampleResult));
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenCompletingFromFailed()
    {
        var prompt = FailedPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.Complete(SampleResult));
    }

    // --- Fail ---

    [Fact]
    public void Should_SetErrorAndFail_WhenFailingFromPending()
    {
        var prompt = PendingPrompt();

        prompt.Fail(SampleError);

        Assert.Equal(SampleError, prompt.ErrorMessage);
        Assert.Equal(PromptStatus.Failed, prompt.Status);
    }

    [Fact]
    public void Should_SetErrorAndFail_WhenFailingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        prompt.Fail(SampleError);

        Assert.Equal(SampleError, prompt.ErrorMessage);
        Assert.Equal(PromptStatus.Failed, prompt.Status);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenFailingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.Fail(SampleError));
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenFailingFromFailed()
    {
        var prompt = FailedPrompt();

        Assert.Throws<InvalidOperationException>(() => prompt.Fail(SampleError));
    }
}
