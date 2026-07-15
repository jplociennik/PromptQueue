using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using PromptQueue.Domain.Prompts;
using PromptQueue.TestSupport;
using PromptQueue.Worker;

namespace PromptQueue.Worker.UnitTests;

/// <summary>Testy jednostkowe logiki procesora: przetwarzanie, pusta odpowiedź, ponowienie, timeout, anulowanie, recovery i readiness.</summary>
public class PromptProcessorTests
{
    // Bez opóźnień w testach: polling i backoff = 0 s.
    private static WorkerOptions FastOptions() =>
        new() { PollIntervalSeconds = 0, RetryDelaySeconds = 0, BatchSize = 10 };

    private static PromptProcessor CreateProcessor(IPromptRepository repository, IChatClient chatClient) =>
        new(repository, chatClient, FastOptions(), NullLogger<PromptProcessor>.Instance);

    [Fact]
    public async Task Should_CompletePrompt_WhenModelReturnsText()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        var chatClient = FakeChatClient.Returning("wynik");
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        prompt.Status.Should().Be(PromptStatus.Completed);
        prompt.Result.Should().Be("wynik");
        chatClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Should_FailPrompt_WhenModelReturnsEmptyResponse()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        var chatClient = FakeChatClient.Returning("   ");
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        prompt.Status.Should().Be(PromptStatus.Failed);
        prompt.ErrorMessage.Should().Be("Model returned an empty response.");
        chatClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Should_CompletePrompt_WhenFirstAttemptFailsAndRetrySucceeds()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        var chatClient = new FakeChatClient(call => call == 1 ? throw new HttpRequestException("transient") : "wynik");
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        prompt.Status.Should().Be(PromptStatus.Completed);
        prompt.Result.Should().Be("wynik");
        chatClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Should_FailPrompt_WhenBothAttemptsFail()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        var chatClient = FakeChatClient.Throwing(new HttpRequestException("model down"));
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        prompt.Status.Should().Be(PromptStatus.Failed);
        prompt.ErrorMessage.Should().Be("model down");
        chatClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Should_FailPrompt_WhenModelTimesOutAndTokenNotCancelled()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        var chatClient = FakeChatClient.Throwing(new TaskCanceledException("request timed out"));
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        prompt.Status.Should().Be(PromptStatus.Failed);
        chatClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Should_CompletePrompt_WhenModelTimesOutOnceThenSucceeds()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        var chatClient = new FakeChatClient(call => call == 1 ? throw new TaskCanceledException("timeout") : "wynik");
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        prompt.Status.Should().Be(PromptStatus.Completed);
        prompt.Result.Should().Be("wynik");
        chatClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Should_LeavePromptProcessing_WhenCancelledDuringModelCall()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Pending();
        repository.Add(prompt);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var chatClient = FakeChatClient.Throwing(new OperationCanceledException(cts.Token));
        var processor = CreateProcessor(repository, chatClient);

        var act = async () => await processor.ProcessPendingAsync(cts.Token);

        using var _ = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        prompt.Status.Should().Be(PromptStatus.Processing);
    }

    [Fact]
    public async Task Should_RequeueInterruptedPrompts_WhenRecovering()
    {
        var repository = new InMemoryPromptRepository();
        var prompt = new PromptBuilder().Processing();
        repository.Add(prompt);
        var processor = CreateProcessor(repository, FakeChatClient.Returning("unused"));

        await processor.RequeueInterruptedAsync(CancellationToken.None);

        prompt.Status.Should().Be(PromptStatus.Pending);
    }

    [Fact]
    public async Task Should_ReturnWhenModelBecomesReady()
    {
        var repository = new InMemoryPromptRepository();
        var chatClient = new FakeChatClient(call => call <= 2 ? throw new HttpRequestException("not ready") : "pong");
        var processor = CreateProcessor(repository, chatClient);

        await processor.WaitForModelAsync(CancellationToken.None);

        chatClient.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Should_NotCallModel_WhenQueueIsEmpty()
    {
        var repository = new InMemoryPromptRepository();
        var chatClient = FakeChatClient.Returning("unused");
        var processor = CreateProcessor(repository, chatClient);

        await processor.ProcessPendingAsync(CancellationToken.None);

        chatClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_PropagateException_WhenSaveChangesFails()
    {
        var repository = new InMemoryPromptRepository { ThrowOnSave = true };
        repository.Add(new PromptBuilder().Pending());
        var chatClient = FakeChatClient.Returning("wynik");
        var processor = CreateProcessor(repository, chatClient);

        var act = async () => await processor.ProcessPendingAsync(CancellationToken.None);

        using var _ = new AssertionScope();
        await act.Should().ThrowAsync<InvalidOperationException>();
        chatClient.CallCount.Should().Be(0);            // zapis przy przejęciu padł -> model nie był wołany
        repository.SaveChangesCallCount.Should().Be(1); // brak zapętlenia -> dokładnie jedna próba zapisu
    }
}
