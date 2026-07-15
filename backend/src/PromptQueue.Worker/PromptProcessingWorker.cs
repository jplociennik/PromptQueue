using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PromptQueue.Worker;

/// <summary>Pętla hosta: recovery i gotowość na starcie, polling w interwale, izolowany scope na cykl. Logikę deleguje do PromptProcessor.</summary>
public sealed class PromptProcessingWorker(
    IServiceScopeFactory scopeFactory,
    WorkerOptions options,
    ILogger<PromptProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PromptProcessingWorker started.");
        await RunInScopeAsync(p => p.RequeueInterruptedAsync(stoppingToken), "startup recovery", stoppingToken);
        await RunInScopeAsync(p => p.WaitForModelAsync(stoppingToken), "model readiness wait", stoppingToken);

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunInScopeAsync(p => p.ProcessPendingAsync(stoppingToken), "processing cycle", stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("PromptProcessingWorker stopping.");
    }

    private async Task RunInScopeAsync(Func<PromptProcessor, Task> action, string phase, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<PromptProcessor>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during {Phase}; worker continues.", phase);
        }
    }
}
