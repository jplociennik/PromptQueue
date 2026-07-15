using PromptQueue.Domain.Prompts;

namespace PromptQueue.Api.Prompts;

/// <summary>Rejestruje endpointy HTTP promptów (/api/v1/prompts): dodawanie wsadu i odczyt stanów.</summary>
public static class PromptEndpoints
{
    /// <summary>Mapuje trasy grupy /api/v1/prompts (POST wsadu, lista, pojedynczy po id).</summary>
    public static IEndpointRouteBuilder MapPromptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/prompts");
        group.MapPost("", CreatePrompts);
        group.MapGet("", GetAllPrompts);
        group.MapGet("{id:guid}", GetPromptById);
        return app;
    }

    private static async Task<IResult> CreatePrompts(
        CreatePromptsRequest request, IPromptRepository repository, CancellationToken cancellationToken)
    {
        var errors = CreatePromptsRequestValidator.Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var prompts = request.Prompts.Select(content => new Prompt(content.Trim())).ToList();
        foreach (var prompt in prompts)
            repository.Add(prompt);
        await repository.SaveChangesAsync(cancellationToken);

        return Results.Ok(new CreatePromptsResponse([.. prompts.Select(p => p.Id)], prompts[0].Status));
    }

    private static async Task<IResult> GetAllPrompts(
        IPromptRepository repository, CancellationToken cancellationToken)
    {
        var prompts = await repository.GetAllAsync(cancellationToken);
        return Results.Ok(prompts.Select(p => p.ToResponse()));
    }

    private static async Task<IResult> GetPromptById(
        Guid id, IPromptRepository repository, CancellationToken cancellationToken)
    {
        var prompt = await repository.GetByIdAsync(id, cancellationToken);
        return prompt is null ? Results.NotFound() : Results.Ok(prompt.ToResponse());
    }
}
