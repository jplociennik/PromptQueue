using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PromptQueue.Domain.Prompts;
using PromptQueue.Infrastructure;
using PromptQueue.Infrastructure.Persistence;
using PromptQueue.Worker;
using Testcontainers.PostgreSql;

namespace PromptQueue.Worker.IntegrationTests;

/// <summary>Fixture integracyjny workera: Postgres w Testcontainerze, realny AddInfrastructure, migracja w setupie; procesor budowany z podmienionym IChatClient.</summary>
public sealed class WorkerTestHost : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private ServiceProvider _services = null!;

    public WorkerOptions Options { get; } = new()
    {
        OllamaBaseUrl = "http://localhost",
        OllamaModel = "test-model",
        PollIntervalSeconds = 0,
        RetryDelaySeconds = 0,
        BatchSize = 10
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PromptQueue"] = _postgres.GetConnectionString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        // Api nie działa w tym harnessie -> migrujemy tu (concern test-harnessu, nie runtime).
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PromptQueueDbContext>().Database.MigrateAsync();
    }

    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    /// <summary>Buduje procesor na realnym repozytorium ze scope oraz podmienionym kliencie modelu.</summary>
    public PromptProcessor CreateProcessor(IServiceProvider serviceProvider, IChatClient chatClient) =>
        new(serviceProvider.GetRequiredService<IPromptRepository>(),
            chatClient,
            Options,
            serviceProvider.GetRequiredService<ILogger<PromptProcessor>>());

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
