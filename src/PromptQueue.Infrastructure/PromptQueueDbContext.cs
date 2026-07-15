using Microsoft.EntityFrameworkCore;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Infrastructure;

/// <summary>Kontekst EF Core kolejki promptów; ładuje konfiguracje encji z własnego assembly.</summary>
public class PromptQueueDbContext(DbContextOptions<PromptQueueDbContext> options) : DbContext(options)
{
    public DbSet<Prompt> Prompts => Set<Prompt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromptQueueDbContext).Assembly);
}
