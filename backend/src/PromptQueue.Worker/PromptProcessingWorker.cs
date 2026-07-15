namespace PromptQueue.Worker;

/// <summary>Odporny szkielet pętli przetwarzania; pełna logika w pq-3.</summary>
public sealed class PromptProcessingWorker(ILogger<PromptProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PromptProcessingWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in processing loop; worker continues.");
            }
        }

        logger.LogInformation("PromptProcessingWorker stopping.");
    }
}
