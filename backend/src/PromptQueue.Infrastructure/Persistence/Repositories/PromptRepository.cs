using Microsoft.EntityFrameworkCore;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Infrastructure.Persistence.Repositories;

/// <summary>Implementacja portu persystencji promptów na EF Core; deterministyczna kolejność odczytu.</summary>
public class PromptRepository(PromptQueueDbContext dbContext) : IPromptRepository
{
    public void Add(Prompt prompt) => dbContext.Prompts.Add(prompt);

    public Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Prompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Prompt>> GetAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.Prompts.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Prompt>> GetByStatusAsync(
        PromptStatus status, int maxCount, CancellationToken cancellationToken = default)
        => await dbContext.Prompts.Where(p => p.Status == status)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(maxCount).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
