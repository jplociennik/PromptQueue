using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptQueue.Domain.Prompts;
using PromptQueue.Infrastructure.Persistence;
using PromptQueue.Infrastructure.Persistence.Repositories;

namespace PromptQueue.Infrastructure;

/// <summary>Rejestracja warstwy Infrastructure: DbContext (Npgsql) i repozytorium promptów.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PromptQueue")
            ?? throw new InvalidOperationException("Connection string 'PromptQueue' is not configured.");

        services.AddDbContext<PromptQueueDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IPromptRepository, PromptRepository>();
        return services;
    }
}
