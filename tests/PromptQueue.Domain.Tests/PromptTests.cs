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
    public void Constructor_InitializesStatusAsPending()
    {
        // Arrange & Act
        var prompt = new Prompt(SampleContent);

        // Assert
        Assert.Equal(PromptStatus.Pending, prompt.Status);
    }

    [Fact]
    public void Constructor_SetsCreatedAtEqualToUpdatedAt()
    {
        // Arrange & Act
        var prompt = new Prompt(SampleContent);

        // Assert
        Assert.Equal(prompt.CreatedAt, prompt.UpdatedAt);
    }

    [Fact]
    public void Constructor_GeneratesNonEmptyId()
    {
        // Arrange & Act
        var prompt = new Prompt(SampleContent);

        // Assert
        Assert.NotEqual(Guid.Empty, prompt.Id);
    }

    [Fact]
    public void Constructor_GeneratesUniqueIdPerInstance()
    {
        // Arrange & Act
        var first = new Prompt(SampleContent);
        var second = new Prompt(SampleContent);

        // Assert
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenContentIsNullOrWhitespace_ThrowsArgumentException(string? content)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Prompt(content!));
    }

    // --- StartProcessing ---

    [Fact]
    public void StartProcessing_FromPending_TransitionsToProcessing()
    {
        // Arrange
        var prompt = PendingPrompt();

        // Act
        prompt.StartProcessing();

        // Assert
        Assert.Equal(PromptStatus.Processing, prompt.Status);
    }

    [Fact]
    public void StartProcessing_FromPending_LeavesUpdatedAtNotBeforeCreatedAt()
    {
        // Arrange
        var prompt = PendingPrompt();

        // Act
        prompt.StartProcessing();

        // Assert (>= a nie >: rozdzielczość zegara może dać identyczny znacznik)
        Assert.True(prompt.UpdatedAt >= prompt.CreatedAt);
    }

    [Fact]
    public void StartProcessing_WhenAlreadyProcessing_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = ProcessingPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.StartProcessing());
    }

    [Fact]
    public void StartProcessing_FromCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = CompletedPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.StartProcessing());
    }

    [Fact]
    public void StartProcessing_FromFailed_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = FailedPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.StartProcessing());
    }

    // --- Complete ---

    [Fact]
    public void Complete_FromProcessing_SetsResultAndTransitionsToCompleted()
    {
        // Arrange
        var prompt = ProcessingPrompt();

        // Act
        prompt.Complete(SampleResult);

        // Assert
        Assert.Equal(SampleResult, prompt.Result);
        Assert.Equal(PromptStatus.Completed, prompt.Status);
    }

    [Fact]
    public void Complete_WhenPending_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = PendingPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.Complete(SampleResult));
    }

    [Fact]
    public void Complete_FromCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = CompletedPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.Complete(SampleResult));
    }

    [Fact]
    public void Complete_FromFailed_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = FailedPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.Complete(SampleResult));
    }

    // --- Fail ---

    [Fact]
    public void Fail_FromPending_SetsErrorAndTransitionsToFailed()
    {
        // Arrange
        var prompt = PendingPrompt();

        // Act
        prompt.Fail(SampleError);

        // Assert
        Assert.Equal(SampleError, prompt.ErrorMessage);
        Assert.Equal(PromptStatus.Failed, prompt.Status);
    }

    [Fact]
    public void Fail_FromProcessing_SetsErrorAndTransitionsToFailed()
    {
        // Arrange
        var prompt = ProcessingPrompt();

        // Act
        prompt.Fail(SampleError);

        // Assert
        Assert.Equal(SampleError, prompt.ErrorMessage);
        Assert.Equal(PromptStatus.Failed, prompt.Status);
    }

    [Fact]
    public void Fail_FromCompleted_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = CompletedPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.Fail(SampleError));
    }

    [Fact]
    public void Fail_FromFailed_ThrowsInvalidOperationException()
    {
        // Arrange
        var prompt = FailedPrompt();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => prompt.Fail(SampleError));
    }
}
