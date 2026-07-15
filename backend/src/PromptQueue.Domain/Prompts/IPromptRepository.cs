namespace PromptQueue.Domain.Prompts;

/// <summary>Port persystencji promptów; jedyna brama zapisu/odczytu dla procesów (Api, Worker).</summary>
public interface IPromptRepository
{
    void Add(Prompt prompt);
    Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Prompt>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Prompt>> GetByStatusAsync(PromptStatus status, int maxCount, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
