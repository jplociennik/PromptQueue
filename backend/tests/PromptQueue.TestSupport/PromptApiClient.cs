using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PromptQueue.Api.Prompts;

namespace PromptQueue.TestSupport;

/// <summary>Typowany klient testowy endpointów /api/v1/prompts: hermetyzuje HttpClient, opcje JSON i ścieżki.</summary>
public sealed class PromptApiClient(HttpClient client)
{
    private const string BasePath = "/api/v1/prompts";

    // Dopasowane do serializacji API: camelCase + enum jako string camelCase (np. "pending").
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>POST wsadu promptów; zwraca surową odpowiedź (do asercji statusu / ProblemDetails).</summary>
    public Task<HttpResponseMessage> PostAsync(CreatePromptsRequest request)
        => client.PostAsJsonAsync(BasePath, request, JsonOptions);

    /// <summary>GET pojedynczego promptu po id; zwraca surową odpowiedź.</summary>
    public Task<HttpResponseMessage> GetByIdAsync(Guid id)
        => client.GetAsync($"{BasePath}/{id}");

    /// <summary>GET listy wszystkich promptów; zwraca surową odpowiedź.</summary>
    public Task<HttpResponseMessage> GetAllAsync()
        => client.GetAsync(BasePath);

    /// <summary>Deserializuje ciało odpowiedzi na wskazany typ kontraktu (opcje JSON zgodne z API).</summary>
    public Task<T?> ReadAsync<T>(HttpResponseMessage response)
        => response.Content.ReadFromJsonAsync<T>(JsonOptions);
}
