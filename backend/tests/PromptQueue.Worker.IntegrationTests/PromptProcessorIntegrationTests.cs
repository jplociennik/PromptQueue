using Microsoft.Extensions.DependencyInjection;
using PromptQueue.Domain.Prompts;
using PromptQueue.TestSupport;

namespace PromptQueue.Worker.IntegrationTests;

/// <summary>Testy integracyjne procesora na żywym Postgresie: przetwarzanie, błąd, filtr statusu i recovery.</summary>
public sealed class PromptProcessorIntegrationTests(WorkerTestHost host) : IClassFixture<WorkerTestHost>
{
    [Fact]
    public async Task Should_CompletePromptWithResult_WhenModelReturnsText()
    {
        Guid id;
        await using (var scope = host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPromptRepository>();
            var prompt = new PromptBuilder().Pending();
            repository.Add(prompt);
            await repository.SaveChangesAsync();
            id = prompt.Id;

            var processor = host.CreateProcessor(scope.ServiceProvider, FakeChatClient.Returning("wynik modelu"));
            await processor.ProcessPendingAsync(CancellationToken.None);
        }

        await using var verifyScope = host.CreateScope();
        var reloaded = await verifyScope.ServiceProvider.GetRequiredService<IPromptRepository>().GetByIdAsync(id);

        using var _ = new AssertionScope();
        reloaded!.Status.Should().Be(PromptStatus.Completed);
        reloaded.Result.Should().Be("wynik modelu");
    }

    [Fact]
    public async Task Should_FailPromptWithError_WhenModelThrows()
    {
        Guid id;
        await using (var scope = host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPromptRepository>();
            var prompt = new PromptBuilder().Pending();
            repository.Add(prompt);
            await repository.SaveChangesAsync();
            id = prompt.Id;

            var processor = host.CreateProcessor(scope.ServiceProvider, FakeChatClient.Throwing(new HttpRequestException("ollama offline")));
            await processor.ProcessPendingAsync(CancellationToken.None);
        }

        await using var verifyScope = host.CreateScope();
        var reloaded = await verifyScope.ServiceProvider.GetRequiredService<IPromptRepository>().GetByIdAsync(id);

        using var _ = new AssertionScope();
        reloaded!.Status.Should().Be(PromptStatus.Failed);
        reloaded.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Should_OnlyProcessPendingPrompts_WhenCompletedPromptExists()
    {
        Guid pendingId, completedId;
        await using (var scope = host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPromptRepository>();
            var completed = new PromptBuilder().WithContent("już gotowe").Completed("oryginalny wynik");
            var pending = new PromptBuilder().WithContent("do zrobienia").Pending();
            repository.Add(completed);
            repository.Add(pending);
            await repository.SaveChangesAsync();
            completedId = completed.Id;
            pendingId = pending.Id;

            var processor = host.CreateProcessor(scope.ServiceProvider, FakeChatClient.Returning("nowy wynik"));
            await processor.ProcessPendingAsync(CancellationToken.None);
        }

        await using var verifyScope = host.CreateScope();
        var verifyRepository = verifyScope.ServiceProvider.GetRequiredService<IPromptRepository>();
        var completedReloaded = await verifyRepository.GetByIdAsync(completedId);
        var pendingReloaded = await verifyRepository.GetByIdAsync(pendingId);

        using var _ = new AssertionScope();
        completedReloaded!.Result.Should().Be("oryginalny wynik");
        pendingReloaded!.Status.Should().Be(PromptStatus.Completed);
        pendingReloaded.Result.Should().Be("nowy wynik");
    }

    [Fact]
    public async Task Should_RequeueInterruptedPrompt_WhenRecovering()
    {
        Guid id;
        await using (var scope = host.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPromptRepository>();
            var prompt = new PromptBuilder().Processing();
            repository.Add(prompt);
            await repository.SaveChangesAsync();
            id = prompt.Id;

            var processor = host.CreateProcessor(scope.ServiceProvider, FakeChatClient.Returning("unused"));
            await processor.RequeueInterruptedAsync(CancellationToken.None);
        }

        await using var verifyScope = host.CreateScope();
        var reloaded = await verifyScope.ServiceProvider.GetRequiredService<IPromptRepository>().GetByIdAsync(id);

        reloaded!.Status.Should().Be(PromptStatus.Pending);
    }
}
