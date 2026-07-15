using PromptQueue.Domain.Prompts;

namespace PromptQueue.Domain.UnitTests;

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

        prompt.Status.Should().Be(PromptStatus.Pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ThrowArgumentException_WhenContentIsBlank(string? content)
    {
        Action act = () => new Prompt(content!);

        act.Should().Throw<ArgumentException>();
    }

    // --- StartProcessing ---

    [Fact]
    public void Should_TransitionToProcessing_WhenStartingFromPending()
    {
        var prompt = PendingPrompt();

        prompt.StartProcessing();

        prompt.Status.Should().Be(PromptStatus.Processing);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenStartingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        Action act = () => prompt.StartProcessing();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenStartingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Action act = () => prompt.StartProcessing();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenStartingFromFailed()
    {
        var prompt = FailedPrompt();

        Action act = () => prompt.StartProcessing();

        act.Should().Throw<InvalidOperationException>();
    }

    // --- Complete ---

    [Fact]
    public void Should_SetResultAndComplete_WhenCompletingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        prompt.Complete(SampleResult);

        using var _ = new AssertionScope();
        prompt.Result.Should().Be(SampleResult);
        prompt.Status.Should().Be(PromptStatus.Completed);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenCompletingFromPending()
    {
        var prompt = PendingPrompt();

        Action act = () => prompt.Complete(SampleResult);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenCompletingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Action act = () => prompt.Complete(SampleResult);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenCompletingFromFailed()
    {
        var prompt = FailedPrompt();

        Action act = () => prompt.Complete(SampleResult);

        act.Should().Throw<InvalidOperationException>();
    }

    // --- Fail ---

    [Fact]
    public void Should_SetErrorAndFail_WhenFailingFromPending()
    {
        var prompt = PendingPrompt();

        prompt.Fail(SampleError);

        using var _ = new AssertionScope();
        prompt.ErrorMessage.Should().Be(SampleError);
        prompt.Status.Should().Be(PromptStatus.Failed);
    }

    [Fact]
    public void Should_SetErrorAndFail_WhenFailingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        prompt.Fail(SampleError);

        using var _ = new AssertionScope();
        prompt.ErrorMessage.Should().Be(SampleError);
        prompt.Status.Should().Be(PromptStatus.Failed);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenFailingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Action act = () => prompt.Fail(SampleError);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenFailingFromFailed()
    {
        var prompt = FailedPrompt();

        Action act = () => prompt.Fail(SampleError);

        act.Should().Throw<InvalidOperationException>();
    }

    // --- Requeue ---

    [Fact]
    public void Should_TransitionToPending_WhenRequeuingFromProcessing()
    {
        var prompt = ProcessingPrompt();

        prompt.Requeue();

        prompt.Status.Should().Be(PromptStatus.Pending);
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenRequeuingFromPending()
    {
        var prompt = PendingPrompt();

        Action act = () => prompt.Requeue();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenRequeuingFromCompleted()
    {
        var prompt = CompletedPrompt();

        Action act = () => prompt.Requeue();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ThrowInvalidOperation_WhenRequeuingFromFailed()
    {
        var prompt = FailedPrompt();

        Action act = () => prompt.Requeue();

        act.Should().Throw<InvalidOperationException>();
    }
}
