using PromptQueue.Domain.Prompts;

namespace PromptQueue.Worker.UnitTests;

/// <summary>Repozytorium promptów w pamięci (lista) do testów jednostkowych procesora; bez bazy, kolejność jak w EF.</summary>
internal sealed class InMemoryPromptRepository : IPromptRepository
{
    private readonly List<Prompt> _prompts = [];

    /// <summary>Gdy true, każdy SaveChangesAsync rzuca — symulacja awarii infrastruktury/DB.</summary>
    public bool ThrowOnSave { get; set; }

    /// <summary>Liczba wywołań SaveChangesAsync — pozwala wykryć zapętlenie cyklu w testach.</summary>
    public int SaveChangesCallCount { get; private set; }

    public void Add(Prompt prompt) => _prompts.Add(prompt);

    public Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_prompts.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Prompt>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Prompt>>(
            [.. _prompts.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)]);

    public Task<IReadOnlyList<Prompt>> GetByStatusAsync(
        PromptStatus status, int maxCount, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Prompt>>(
            [.. _prompts.Where(p => p.Status == status).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(maxCount)]);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return ThrowOnSave
            ? Task.FromException(new InvalidOperationException("Simulated database failure."))
            : Task.CompletedTask;
    }
}
