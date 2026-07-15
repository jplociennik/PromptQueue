using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PromptQueue.Infrastructure;

/// <summary>Fabryka DbContext dla narzędzi EF (dotnet ef); czyta connection string z env var (fail-fast).</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PromptQueueDbContext>
{
    public PromptQueueDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PromptQueue")
            ?? throw new InvalidOperationException("Environment variable 'ConnectionStrings__PromptQueue' is not set.");

        var options = new DbContextOptionsBuilder<PromptQueueDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PromptQueueDbContext(options);
    }
}
