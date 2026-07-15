using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PromptQueue.Infrastructure.Persistence;

/// <summary>Aplikacja migracji na starcie hosta; loguje przebieg i fail-fast (rethrow) przy błędzie.</summary>
public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PromptQueueDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PromptQueueDbContext>>();

        try
        {
            logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migrations applied.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply database migrations.");
            throw;
        }
    }
}
