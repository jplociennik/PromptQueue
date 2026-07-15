using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Worker;

/// <summary>Gotowość modelu, przejęcie promptu, wywołanie z jednym ponowieniem, zapis wyniku/błędu i recovery. Testowalna jednostka (scoped).</summary>
public sealed class PromptProcessor(
    IPromptRepository repository,
    IChatClient chatClient,
    WorkerOptions options,
    ILogger<PromptProcessor> logger)
{
    private const int MaxErrorLength = 2_000;
    private const int EscalateAfterAttempts = 10;

    /// <summary>Czeka aż model odpowie (readiness na starcie); anulowalne przez shutdown.</summary>
    public async Task WaitForModelAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await chatClient.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, cancellationToken);
                logger.LogInformation("Model endpoint is ready.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                // Po dłuższym oczekiwaniu eskaluj log, by odróżnić wolny pull modelu od błędnej nazwy w konfiguracji.
                if (attempt >= EscalateAfterAttempts)
                    logger.LogError(
                        "Model endpoint still not ready after {Attempt} attempts ({Message}); verify Worker:OllamaModel. Retrying in {Delay}s.",
                        attempt, ex.Message, options.PollIntervalSeconds);
                else
                    logger.LogWarning("Model endpoint not ready ({Message}); retrying in {Delay}s.",
                        ex.Message, options.PollIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), cancellationToken);
            }
        }
    }

    /// <summary>Recovery: zwraca przerwane prompty (Processing) z powrotem do kolejki (Pending).</summary>
    public async Task RequeueInterruptedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var interrupted = await repository.GetByStatusAsync(PromptStatus.Processing, options.BatchSize, cancellationToken);
            if (interrupted.Count == 0)
                return;

            foreach (var prompt in interrupted)
                prompt.Requeue();
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Drenuje kolejkę Pending batchami: przejęcie, wywołanie modelu z ponowieniem, finalizacja.</summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pending = await repository.GetByStatusAsync(PromptStatus.Pending, options.BatchSize, cancellationToken);
            if (pending.Count == 0)
                return;

            // Świadomie BEZ guardu per-prompt (cofa K2 krytyka): przy pojedynczym workerze guard nie chronił
            // przed niczym realnym, a łykał też wyjątki infrastruktury (np. SaveChanges) -> stęchła instancja
            // Processing w change-trackerze -> tight-loop bez Delay (O1). Błędy MODELU są łapane wewnątrz
            // ProcessAsync (-> Fail), więc feralna treść nie przerywa cyklu; wyjątki DB/domenowe słusznie
            // bąbelkują do RunInScopeAsync (kolejny cykl dostaje świeży scope/DbContext).
            foreach (var prompt in pending)
                await ProcessAsync(prompt, cancellationToken);
        }
    }

    private async Task ProcessAsync(Prompt prompt, CancellationToken cancellationToken)
    {
        prompt.StartProcessing();
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await InvokeModelWithRetryAsync(prompt.Content, cancellationToken);
            if (string.IsNullOrWhiteSpace(result))
                prompt.Fail("Model returned an empty response.");
            else
                prompt.Complete(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model call failed for prompt {PromptId}.", prompt.Id);
            prompt.Fail(ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message);
        }

        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> InvokeModelWithRetryAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            return (await chatClient.GetResponseAsync(content, cancellationToken: cancellationToken)).Text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model call failed; retrying once in {Delay}s.", options.RetryDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), cancellationToken);
            return (await chatClient.GetResponseAsync(content, cancellationToken: cancellationToken)).Text;
        }
    }
}
