using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PromptQueue.Api.Prompts;
using PromptQueue.Domain.Prompts;
using PromptQueue.TestSupport;

namespace PromptQueue.Api.IntegrationTests;

/// <summary>Testy integracyjne endpointów promptów (HTTP + Postgres): happy path, trym, kolejność, walidacja, 404 i ścieżka 500.</summary>
public sealed class PromptEndpointsTests(PromptApiFactory factory) : IClassFixture<PromptApiFactory>
{
    private PromptApiClient CreateApi() => new(factory.CreateClient());

    [Fact]
    public async Task Should_CreateBatchAsPending_WhenPostingPrompts()
    {
        var api = CreateApi();
        var request = new CreatePromptsRequestBuilder().WithPrompts("Prompt jeden", "Prompt dwa").Build();

        var result = await PostAndFetchFirstAsync(api, request);

        using var _ = new AssertionScope();
        result.PostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Created.Ids.Should().HaveCount(2);
        result.Created.Status.Should().Be(PromptStatus.Pending);
        result.GetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Fetched.Content.Should().Be("Prompt jeden");
        result.Fetched.Status.Should().Be(PromptStatus.Pending);
    }

    [Fact]
    public async Task Should_TrimContent_WhenPostingPromptWithSurroundingWhitespace()
    {
        var api = CreateApi();
        var request = new CreatePromptsRequestBuilder().WithPrompts("  streść tekst  ").Build();

        var result = await PostAndFetchFirstAsync(api, request);

        using var _ = new AssertionScope();
        result.PostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        result.GetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Fetched.Content.Should().Be("streść tekst");
    }

    [Fact]
    public async Task Should_ReturnListOrderedByCreatedAtThenId_WhenGettingAll()
    {
        var api = CreateApi();
        var request = new CreatePromptsRequestBuilder().WithPrompts("A", "B", "C", "D").Build();

        var postResponse = await api.PostAsync(request);
        var created = await api.ReadAsync<CreatePromptsResponse>(postResponse);
        var ownIds = created!.Ids.ToHashSet();
        var listResponse = await api.GetAllAsync();
        var all = await api.ReadAsync<List<PromptResponse>>(listResponse);
        var mine = all!.Where(p => ownIds.Contains(p.Id)).ToList();
        var expectedOrder = mine.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Select(p => p.Id).ToList();

        using var _ = new AssertionScope();
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mine.Should().HaveCount(ownIds.Count);
        mine.Select(p => p.Id).Should().Equal(expectedOrder);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WhenPostingEmptyList()
    {
        var api = CreateApi();
        var request = new CreatePromptsRequestBuilder().WithNoPrompts().Build();

        var response = await api.PostAsync(request);

        await response.ShouldBeValidationProblemAsync("prompts");
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WhenPostingOversizedPrompt()
    {
        var api = CreateApi();
        var request = new CreatePromptsRequestBuilder().WithOversizedPrompt().Build();

        var response = await api.PostAsync(request);

        await response.ShouldBeValidationProblemAsync("prompts[0]");
    }

    [Fact]
    public async Task Should_ReturnNotFound_WhenPromptDoesNotExist()
    {
        var api = CreateApi();

        var response = await api.GetByIdAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnProblemDetails_WhenRepositoryThrowsInProduction()
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPromptRepository>();
                services.AddScoped<IPromptRepository, ThrowingPromptRepository>();
            });
        }).CreateClient();
        var api = new PromptApiClient(client);

        var response = await api.GetAllAsync();

        await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError);
    }

    // Wspólny flow: dodaje wsad i pobiera pierwszy utworzony prompt po id (zwraca surowe odpowiedzi do asercji).
    private static async Task<PostAndFetch> PostAndFetchFirstAsync(PromptApiClient api, CreatePromptsRequest request)
    {
        var postResponse = await api.PostAsync(request);
        var created = await api.ReadAsync<CreatePromptsResponse>(postResponse);
        var getResponse = await api.GetByIdAsync(created!.Ids[0]);
        var fetched = await api.ReadAsync<PromptResponse>(getResponse);
        return new PostAndFetch(postResponse, created, getResponse, fetched!);
    }

    private sealed record PostAndFetch(
        HttpResponseMessage PostResponse,
        CreatePromptsResponse Created,
        HttpResponseMessage GetResponse,
        PromptResponse Fetched);

    /// <summary>Repozytorium rzucające na każdą operację — wymusza ścieżkę GlobalExceptionHandler (500).</summary>
    private sealed class ThrowingPromptRepository : IPromptRepository
    {
        public void Add(Prompt prompt) => throw new InvalidOperationException("Database unavailable.");

        public Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Database unavailable.");

        public Task<IReadOnlyList<Prompt>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Database unavailable.");

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Database unavailable.");
    }
}
