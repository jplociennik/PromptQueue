# Projekt: pq-2 — API: dodawanie promptów + odczyt statusów

> Data: 2026-07-15
> Wersja: 2
> Werdykt: AKCEPTUJ (krytyka v2 bez uwag istotnych; drobne naniesione)

## Cel

Dołożyć warstwę HTTP nad istniejącym fundamentem (pq-1): trzy endpointy REST, przez które front (pq-4/pq-5) zgłasza wsadowo prompty i odpytuje ich stan. Źródło wymagań: [DoD → L3](../prepare-projects/DoD.md#L3) (dodawanie + pobieranie stanów), [DoD → L7](../prepare-projects/DoD.md#L7) (lista dla frontu), [pq-2 → L26](../prepare-projects/pq-2.md#L26) (cienka warstwa nad repozytorium). Zakres to **wyłącznie** `PromptQueue.Api` + testy — bez zmian w Domain/Infrastructure, bez migracji, bez workera/frontu/compose. Task realnie weryfikuje `GlobalExceptionHandler`, który wszedł w pq-1 nieuruchomiony ([pq-1 → L413](pq-1.md#L413)), i ustanawia konwencję testów (podział unit/integration + buildery/asercje) jako fundament dla pq-3+.

## Proponowana architektura

Endpointy Minimal API **są** tą „cienką warstwą aplikacji" — wołają `IPromptRepository` (szew z pq-1) bez pośredniej klasy serwisu (patrz § Kluczowe decyzje). Jedyna nietrywialna logika — walidacja wsadu — wychodzi do czystej, testowalnej jednostki. Wszystko w feature-folderze `Prompts/`, spójnie z układem `Prompts/` w Domain.

Podział odpowiedzialności w `PromptQueue.Api/Prompts/`:
- **Contracts** — rekordy DTO żądań/odpowiedzi (kontrakt HTTP, oddzielony od encji domenowej).
- **Validator** — SSOT limitów wsadu; czysta funkcja `request → słownik błędów`.
- **Mapping** — projekcja `Prompt → PromptResponse` (encja nie wycieka na zewnątrz).
- **Endpoints** — routing, wiązanie, trymowanie treści, kody HTTP; deleguje do walidatora, repozytorium i mappingu.

### Kluczowe abstrakcje (kod)

Kontrakty — `Prompts` to lista treści (zgodnie z [pq-2 → L14](../prepare-projects/pq-2.md#L14)); `Ids` typu `Guid` serializują się jako stringi (handoff [pq-1 → L405](pq-1.md#L405)):

```csharp
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Api.Prompts;

public record CreatePromptsRequest(IReadOnlyList<string> Prompts);

public record CreatePromptsResponse(IReadOnlyList<Guid> Ids, PromptStatus Status);

public record PromptResponse(
    Guid Id,
    string Content,
    PromptStatus Status,
    string? Result,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

Walidator — SSOT limitów; puste/za długie odrzuca na granicy HTTP, zanim powstanie encja (bramka `Prompt` zostaje fail-loud backstopem). Długość liczona po trymowaniu (białe znaki brzegowe nie zjadają limitu). Typ zwracany `Dictionary` — wymagany przez `Results.ValidationProblem`:

```csharp
namespace PromptQueue.Api.Prompts;

/// <summary>Walidacja batch-POST: rozmiar listy i pojedynczego promptu (długość po trymowaniu). SSOT limitów żądania.</summary>
public static class CreatePromptsRequestValidator
{
    public const int MaxPromptsPerRequest = 50;
    public const int MaxPromptLength = 8_000;

    public static Dictionary<string, string[]> Validate(CreatePromptsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Prompts is null || request.Prompts.Count == 0)
        {
            errors["prompts"] = ["At least one prompt is required."];
            return errors;
        }

        if (request.Prompts.Count > MaxPromptsPerRequest)
            errors["prompts"] = [$"A single request accepts at most {MaxPromptsPerRequest} prompts."];

        for (var i = 0; i < request.Prompts.Count; i++)
        {
            var content = request.Prompts[i];
            if (string.IsNullOrWhiteSpace(content))
                errors[$"prompts[{i}]"] = ["Prompt must not be empty."];
            else if (content.Trim().Length > MaxPromptLength)
                errors[$"prompts[{i}]"] = [$"Prompt must not exceed {MaxPromptLength} characters."];
        }

        return errors;
    }
}
```

Mapping — projekcja encji na DTO:

```csharp
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Api.Prompts;

public static class PromptMapping
{
    public static PromptResponse ToResponse(this Prompt prompt) => new(
        prompt.Id, prompt.Content, prompt.Status,
        prompt.Result, prompt.ErrorMessage, prompt.CreatedAt, prompt.UpdatedAt);
}
```

Endpointy — grupa `/api/v1/prompts`, wiązanie `{id:guid}` (handoff [pq-1 → L405](pq-1.md#L405)); handlery cienkie, DI przez parametry. Encja tworzona z **trymowanej** treści (Domain nietknięty); status w odpowiedzi POST czytany z encji, nie literał (SSOT):

```csharp
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Api.Prompts;

public static class PromptEndpoints
{
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
```

Delta `Program.cs` — enum jako camelCase-string (dla `StatusBadge` w pq-5), rejestracja endpointów, `Program` dostępny testom (dopiski do istniejącego `backend/src/PromptQueue.Api/Program.cs`):

```csharp
using System.Text.Json.Serialization;

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

app.MapPromptEndpoints();

public partial class Program;
```

Builder żądania (shared, `PromptQueue.TestSupport`) — jedno miejsce budowy `CreatePromptsRequest` dla testów jednostkowych i integracyjnych; centralizuje wartości brzegowe (limit liczby, prompt ponadwymiarowy):

```csharp
using PromptQueue.Api.Prompts;

namespace PromptQueue.TestSupport;

/// <summary>Buduje CreatePromptsRequest dla testów; sensowne domyślne + warianty brzegowe.</summary>
public sealed class CreatePromptsRequestBuilder
{
    private readonly List<string> _prompts = ["Przetłumacz ten akapit na język francuski."];

    public CreatePromptsRequestBuilder WithPrompts(params string[] prompts)
    {
        _prompts.Clear();
        _prompts.AddRange(prompts);
        return this;
    }

    public CreatePromptsRequestBuilder WithNoPrompts()
    {
        _prompts.Clear();
        return this;
    }

    public CreatePromptsRequestBuilder WithPromptCount(int count)
    {
        _prompts.Clear();
        _prompts.AddRange(Enumerable.Range(1, count).Select(i => $"Prompt {i}"));
        return this;
    }

    public CreatePromptsRequestBuilder WithOversizedPrompt()
    {
        _prompts.Clear();
        _prompts.Add(new string('x', CreatePromptsRequestValidator.MaxPromptLength + 1));
        return this;
    }

    public CreatePromptsRequest Build() => new(_prompts);
}
```

Asercje HTTP (shared, `PromptQueue.TestSupport`) — standaryzują sprawdzenie kształtu ProblemDetails/ValidationProblem, używane przez testy integracyjne:

```csharp
using System.Net;
using System.Text.Json;

namespace PromptQueue.TestSupport;

/// <summary>Asercje na odpowiedziach HTTP: kształt ValidationProblem (400) i ProblemDetails (np. 500).</summary>
public static class ProblemDetailsAssertions
{
    public static async Task ShouldBeValidationProblemAsync(this HttpResponseMessage response, string expectedErrorKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(expectedErrorKey, out _));
    }

    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
```

Fixture testów integracyjnych (`PromptQueue.Api.IntegrationTests`) — Testcontainer musi żyć **przed** budową hosta (fail-fast connection stringa w `AddInfrastructure`), stąd `IAsyncLifetime` + leniwe `UseSetting`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace PromptQueue.Api.IntegrationTests;

/// <summary>Fabryka hosta testowego: Postgres w Testcontainerze, connection string wstrzykiwany przed budową hosta.</summary>
public sealed class PromptApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseSetting("ConnectionStrings:PromptQueue", _postgres.GetConnectionString());

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

## Kontrakt API

Konwencja JSON: camelCase (domyślna .NET web), enum jako string camelCase. Błędy walidacji i błędy nieobsłużone są `application/problem+json` (spójnie: `ValidationProblem` 400 ↔ `GlobalExceptionHandler` 500). Treść promptu jest trymowana przy przyjęciu — persystowana i zwracana bez białych znaków brzegowych.

| Endpoint | Żądanie | Sukces | Błędy |
|---|---|---|---|
| `POST /api/v1/prompts` | `{ "prompts": ["...", "..."] }` | `200` `{ "ids": ["<guid>", "<guid>"], "status": "pending" }` | `400` ValidationProblem (pusta lista / pusty / > `MaxPromptLength` po trymie / > `MaxPromptsPerRequest`); `500` |
| `GET /api/v1/prompts` | — | `200` `[ { "id","content","status","result","errorMessage","createdAt","updatedAt" }, … ]` (kolejność `CreatedAt`+`Id` z repo) | `500` |
| `GET /api/v1/prompts/{id:guid}` | — | `200` pojedynczy `PromptResponse` | `404` (brak / id nie-`guid` → trasa niedopasowana); `500` |

`result` niepuste dla `completed`, `errorMessage` niepuste dla `failed` — front rozróżnia po `status` ([pq-5 → L18](../prepare-projects/pq-5.md#L18)).

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---|---|---|
| `CreatePromptsRequest`, `CreatePromptsResponse`, `PromptResponse` (rekordy) | Api | `backend/src/PromptQueue.Api/Prompts/PromptContracts.cs` |
| `CreatePromptsRequestValidator` (+ limity `const`, długość po trymie) | Api | `backend/src/PromptQueue.Api/Prompts/CreatePromptsRequestValidator.cs` |
| `PromptMapping.ToResponse` | Api | `backend/src/PromptQueue.Api/Prompts/PromptMapping.cs` |
| `PromptEndpoints.MapPromptEndpoints` + 3 handlery (`content.Trim()`) | Api | `backend/src/PromptQueue.Api/Prompts/PromptEndpoints.cs` |
| JSON opts + `MapPromptEndpoints()` + `public partial class Program` | Api | `backend/src/PromptQueue.Api/Program.cs` |
| `CreatePromptsRequestBuilder` (builder) | TestSupport | `backend/tests/PromptQueue.TestSupport/CreatePromptsRequestBuilder.cs` |
| `ProblemDetailsAssertions` (asercje HTTP) | TestSupport | `backend/tests/PromptQueue.TestSupport/ProblemDetailsAssertions.cs` |
| Projekt wsparcia (ref. Api, `xunit.assert`) | TestSupport | `backend/tests/PromptQueue.TestSupport/PromptQueue.TestSupport.csproj` |
| Testy jednostkowe walidatora | Tests (unit) | `backend/tests/PromptQueue.Api.Tests/CreatePromptsRequestValidatorTests.cs` |
| Projekt testów jednostkowych (ref. Api, TestSupport, xunit) | Tests (unit) | `backend/tests/PromptQueue.Api.Tests/PromptQueue.Api.Tests.csproj` |
| `PromptApiFactory` (`IAsyncLifetime` + Testcontainer) | Tests (integration) | `backend/tests/PromptQueue.Api.IntegrationTests/PromptApiFactory.cs` |
| Testy integracyjne endpointów (+ trym, kolejność, ścieżka 500) | Tests (integration) | `backend/tests/PromptQueue.Api.IntegrationTests/PromptEndpointsTests.cs` |
| Projekt testów integracyjnych (ref. Api, TestSupport, Mvc.Testing, Testcontainers.PostgreSql, xunit) | Tests (integration) | `backend/tests/PromptQueue.Api.IntegrationTests/PromptQueue.Api.IntegrationTests.csproj` |
| Dodanie 3 projektów do solution | — | `backend/PromptQueue.sln` |

## Przepływ danych

SSOT stanu i `Id` promptu to encja `Prompt` (pq-1); endpointy nic nie liczą o stanie — odczytują go z repozytorium. SSOT limitów żądania to stałe w walidatorze; nie kopiuj ich do frontu ani bazy.

- **POST**: bind `CreatePromptsRequest` → `Validator.Validate` (długość liczona po trymie) → jeśli błędy `400 ValidationProblem` (encje nie powstają) → w przeciwnym razie `new Prompt(content.Trim())` (ctor nadaje `Id` + `Pending`) dla każdej treści → `Add` × N → jeden `SaveChangesAsync` (cały wsad w jednej transakcji EF) → `200` z listą `Id` i statusem z encji. Prompty czekają w stanie `Pending` na workera pq-3, który filtruje po `Status = Pending` ([pq-3 → L16](../prepare-projects/pq-3.md#L16)).
- **GET (lista / po id)**: `GetAllAsync` / `GetByIdAsync` → `ToResponse` → `200` (lub `404`). Lista wraca w porządku `CreatedAt` + `Id` gwarantowanym przez repo (pq-1); odpowiedź odzwierciedla bieżący stan encji zmieniany przez workera — to źródło pollingu pq-5.
- **Ścieżki błędu**: walidacja → `400` (kontrolowane). Nieobsłużony wyjątek (np. padnięcie DB) → `GlobalExceptionHandler` → `500 ProblemDetails`; w `Development` przechwytuje wcześniej `DeveloperExceptionPage`, stąd weryfikacja handlera w env `Production` (patrz § Strategia testowania).

Content w bazie pozostaje `text` bez limitu (pq-1) — limit długości egzekwuje warstwa HTTP, więc **migracja nie jest potrzebna** (zakres pq-2 = tylko HTTP).

## Zarządzanie stanem

- **Stan promptu** — SSOT: encja `Prompt` w bazie (pq-1). Endpointy są bezstanowe.
- **Limity wsadu** — SSOT: stałe `MaxPromptsPerRequest` / `MaxPromptLength` w walidatorze (builder testowy je czyta, nie kopiuje).
- **Kolejność listy** — SSOT: `PromptRepository.GetAllAsync` (`OrderBy CreatedAt` + `ThenBy Id`), bez duplikowania sortu w API/froncie.
- **Trymowanie treści** — SSOT: granica HTTP (`CreatePrompts`); Domain nie zna reguły trymowania.

## Kluczowe decyzje

- **Limity: `MaxPromptsPerRequest = 50`, `MaxPromptLength = 8000`.** Decyzja użytkownika (2026-07-15). Skala demo; 8000 znaków bezpieczne dla kontekstu modelu w pq-3.
- **Lista pełna, bez paginacji/filtra; kolejność chronologiczna gwarantuje repo.** Decyzja użytkownika (2026-07-15). Na pytanie o `ORDER BY`: sort już istnieje w `PromptRepository.GetAllAsync` z pq-1 (`OrderBy(CreatedAt).ThenBy(Id)`) — kontrakt GET listy zwraca porządek chronologiczny, SSOT sortu w repo, **zero zmian kodu**. Uwaga: w obrębie jednego batcha `CreatedAt` bywa identyczny (ten sam `UtcNow`), więc tie-break `Id` (losowy Guid) nie odtwarza kolejności wpisywania — porządek jest stabilny wg (`CreatedAt`, `Id`), nie wg indeksu w żądaniu. Test kolejności asertuje ten kontrakt (patrz § Strategia testowania).
- **JSON: domyślny camelCase + enum jako string camelCase (`JsonStringEnumConverter`).** Decyzja użytkownika (2026-07-15). `"pending"/"processing"/…` mapuje się wprost na `StatusBadge` (pq-5); snake_case odrzucony (konfiguracja bez zysku).
- **POST zwraca `200`, nie `201 Created`.** Decyzja użytkownika (2026-07-15). Zgodne z [pq-2 → L16](../prepare-projects/pq-2.md#L16); batch nie ma pojedynczego `Location`.
- **CORS odłożony do pq-4/pq-6.** Decyzja użytkownika (2026-07-15). Origin frontu znany dopiero przy froncie/compose; dokładanie teraz to funkcja „na zapas".
- **Trymowanie `Content`: TAK, na granicy HTTP.** Decyzja użytkownika (2026-07-15). Endpoint tworzy `new Prompt(content.Trim())` — Domain nietknięty w pq-2 (SSOT trymowania = warstwa HTTP). Długość walidowana po trymie (`content.Trim().Length`), więc białe znaki brzegowe nie zjadają limitu, a walidacja liczy dokładnie to, co zostanie zapisane; prompt z samych białych znaków trymuje się do pustego → już odrzucony przez `IsNullOrWhiteSpace`. Podwójny `Trim()` (check długości + tworzenie encji) świadomie zaakceptowany: idempotentny i tańszy niż przepychanie znormalizowanej struktury. **Białe znaki wewnętrzne (między słowami) pozostają 1:1** — `Trim()` czyści wyłącznie brzegi: w promptach do LLM formatowanie wewnętrzne (nowe linie, wcięcia, bloki kodu) jest znaczące, a nadużycia ogranicza limit — znaki wewnętrzne liczą się do `MaxPromptLength`.
- **Brak osobnej klasy serwisu — endpointy są cienką warstwą aplikacji.** Serwis nad trzema operacjami byłby dla obu GET-ów pustym przelotem; jedyna logika (walidacja) wyszła do testowalnego `CreatePromptsRequestValidator`. Zgodne z KISS i skillem `code-backend`; interpretacja [pq-2 → L26](../prepare-projects/pq-2.md#L26): „cienka warstwa" = same endpointy. Endpoint filter / FluentValidation odrzucone — nadmiarowe przy jednej regule wsadu.
- **Weryfikacja `GlobalExceptionHandler` przez test integracyjny (env `Production` + stub repo rzucający).** Bez fałszywego endpointu produkcyjnego: `ConfigureTestServices` podmienia `IPromptRepository` na rzucający (nadpisuje rejestrację z `AddInfrastructure`), `GET` → asercja `500` + `application/problem+json`. Realizuje handoff z [pq-1 → L413](pq-1.md#L413). Migracja na starcie hosta używa realnego `DbContext`, więc kontener musi żyć także w tym wariancie.
- **Testy integracyjne na Testcontainers-PostgreSQL** (nie compose, nie InMemory) — izolowana, jednorazowa baza per uruchomienie; InMemory nie oddaje Npgsql/enum-string/`uuid`. Wymaga demona Docker (repo i tak na nim stoi).
- **Izolacja danych między testami integracyjnymi: asercje na własnych `Id`.** Kontener współdzielony w klasie testowej; testy asertują tylko podzbiór utworzony w danym teście (dane innych testów mogą współistnieć). Reset bazy (Respawn/TRUNCATE) — dopiero gdyby zaszła potrzeba asercji na całej liście.
- **Podział testów na projekty (fundament dla pq-3+).** Decyzja użytkownika (2026-07-15). Konwencja: `*.Tests` = jednostkowe (szybkie, bez I/O — jak istniejące `PromptQueue.Domain.Tests`), `*.IntegrationTests` = integracyjne (Docker/Testcontainers). Stąd `PromptQueue.Api.Tests` (walidator) i `PromptQueue.Api.IntegrationTests` (endpointy). Nazwa czytelnie komunikuje typ; pq-3 doda analogicznie `PromptQueue.Worker.Tests` / `…IntegrationTests`.
- **Wzorce testowe: builder + własne asercje w shared `PromptQueue.TestSupport`; reszta odrzucona (fundament dla pq-3+).** Decyzja użytkownika (2026-07-15). **Przyjęte:** `CreatePromptsRequestBuilder` (jedno miejsce budowy żądania dla obu projektów testowych; centralizuje domyślne i wartości brzegowe — limit liczby, prompt ponadwymiarowy — bez kopiowania stałych) i `ProblemDetailsAssertions` (extension methods redukujące powtórzenie asercji ProblemDetails w testach integracyjnych). Shared classlib jest wspólnym domem tych helperów i punktem wzrostu dla pq-3 (np. `PromptBuilder` dla stanów encji w testach workera — świadomie **nie** tworzony teraz, YAGNI). **Odrzucone:** osobna klasa `TestData`/ObjectMother (domyślne mieszczą się w builderze), AutoFixture/AutoData (dane losowe łamią determinizm — skill), FluentAssertions (zbędna zależność wobec `Assert` + 2 extension methods), generyczne bazowe klasy testów poza `PromptApiFactory` (YAGNI). `TestSupport` referuje `xunit.assert` (wszyscy konsumenci to xunit; wersja zgodna z 2.9.3).

## Plan implementacji

1. Dodaj rekordy DTO — `backend/src/PromptQueue.Api/Prompts/PromptContracts.cs`.
2. Dodaj `CreatePromptsRequestValidator` (typ zwracany `Dictionary<string, string[]>`, długość po `Trim()`) — `backend/src/PromptQueue.Api/Prompts/CreatePromptsRequestValidator.cs`.
3. Dodaj `PromptMapping.ToResponse` — `backend/src/PromptQueue.Api/Prompts/PromptMapping.cs`.
4. Dodaj `PromptEndpoints` z grupą `/api/v1/prompts`, 3 handlerami i `new Prompt(content.Trim())` — `backend/src/PromptQueue.Api/Prompts/PromptEndpoints.cs`.
5. W `Program.cs` dołóż `ConfigureHttpJsonOptions` (enum camelCase-string), `app.MapPromptEndpoints()` i `public partial class Program;` — `backend/src/PromptQueue.Api/Program.cs`.
6. Utwórz classlib `PromptQueue.TestSupport` (ref. Api, `xunit.assert` **przypięte = 2.9.3**, jak metapakiet `xunit` w projektach testowych) z `CreatePromptsRequestBuilder` i `ProblemDetailsAssertions`; dodaj do solution — `backend/tests/PromptQueue.TestSupport/`.
7. Utwórz projekt jednostkowy `PromptQueue.Api.Tests` (ref. Api, TestSupport, xunit jak w `PromptQueue.Domain.Tests.csproj`) i napisz testy walidatora (`Should_…`, builder) — `backend/tests/PromptQueue.Api.Tests/`.
8. Utwórz projekt integracyjny `PromptQueue.Api.IntegrationTests` (ref. Api, TestSupport, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql` **aktualne 4.x** — konstruktor `PostgreSqlBuilder(string)`; bezparametrowy jest `[Obsolete]`, starsze 3.x go nie mają, xunit) — `backend/tests/PromptQueue.Api.IntegrationTests/`.
9. Napisz `PromptApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` (kontener w `InitializeAsync` przed pierwszym `CreateClient()`; connection string przez `UseSetting` w `ConfigureWebHost`) — `backend/tests/PromptQueue.Api.IntegrationTests/PromptApiFactory.cs`.
10. Napisz testy integracyjne endpointów: happy path, trym, kolejność listy, `400`, `404`, oraz ścieżkę `500` (env `Production` + stub przez `ConfigureTestServices`) — `backend/tests/PromptQueue.Api.IntegrationTests/PromptEndpointsTests.cs`.
11. Dodaj 3 nowe projekty do solution — `backend/PromptQueue.sln`.
12. Weryfikacja: `. $PROFILE.CurrentUserAllHosts; all-build` (0/0), `all-test`; ręcznie `docker compose up -d postgres` + `dotnet run --project backend/src/PromptQueue.Api` → POST/GET przez `curl`/REST client.

## Strategia testowania

- **Walidator — jednostkowe (`PromptQueue.Api.Tests`, xUnit, `Should_…`, builder z TestSupport).** `Should_ReturnError_WhenListIsEmpty` (`WithNoPrompts`), `…WhenListIsNull` (inline `new(null!)` — builder nie wyraża null), `…WhenPromptIsBlank` (`Theory`: null/""/"   "), `…WhenTrimmedPromptExceedsMaxLength` (`WithOversizedPrompt`), `…WhenCountExceedsMax` (`WithPromptCount(MaxPromptsPerRequest + 1)`), `Should_ReturnNoErrors_WhenRequestIsValid` (domyślny build), `Should_ReturnNoErrors_WhenPromptHasSurroundingWhitespaceWithinLimit` (`WithPrompts("  hello  ")`) oraz przypadek brzegowy `Should_ReturnNoErrors_WhenRawLengthExceedsMaxButTrimmedFits` (treść z białymi znakami: raw > `MaxPromptLength`, po trymie ≤ — domyka kontrakt „długość po trymie" od strony bramki). Czysta funkcja, bez HTTP/DB.
- **Endpointy — integracyjne (`PromptQueue.Api.IntegrationTests`, WebApplicationFactory + Testcontainers, asercje na własnych `Id`).** POST wsadu → `200`, liczba `ids` = liczba treści, `status = pending`, a `GET {id}` potwierdza `pending` + `content` (DoD [pq-2 → L32](../prepare-projects/pq-2.md#L32)); **trym**: POST `["  streść tekst  "]` → `GET {id}` zwraca `content == "streść tekst"`; **kolejność**: wstaw kilka promptów, `GET` listy, weź podzbiór własnych `Id` i asertuj, że ich kolejność = ten sam podzbiór posortowany po (`CreatedAt`, `Id`) — deterministyczne, bez zależności od zegara, weryfikuje kontrakt sortu; POST pustej listy → `400` (`ShouldBeValidationProblemAsync("prompts")`), prompt ponadwymiarowy → `400`; `GET {id}` losowy `guid` → `404`.
- **`GlobalExceptionHandler` — integracyjny, obowiązkowy (handoff).** Fabryka z `WithWebHostBuilder`: `UseEnvironment("Production")` + `ConfigureTestServices` podmieniający `IPromptRepository` na rzucający → `GET /api/v1/prompts` → `ShouldBeProblemAsync(InternalServerError)` (`500` + `application/problem+json`).
- **Nie testujemy frameworka** (routing, wiązanie modelu, serializacja) ani trywialnego `ToResponse` — zgodnie ze skillem `code-backend`.

## Pytania do użytkownika

Brak otwartych pytań — wszystkie kwestie z v1 (limity, paginacja/`ORDER BY`, JSON, `200`, CORS, trymowanie) rozstrzygnięte decyzjami użytkownika z 2026-07-15 (patrz § Kluczowe decyzje).

## Krytyka (solution-critic)

**v2 — Werdykt: AKCEPTUJ** (zero uwag krytycznych i istotnych). Wszystkie 3 uwagi ISTOTNE z v1 potwierdzone jako domknięte. Nowe elementy zweryfikowane u źródła:
- `new PostgreSqlBuilder("postgres:18-alpine")` — **poprawny i preferowany idiom** (konstruktor bezparametrowy jest `[Obsolete]` w Testcontainers.PostgreSql; zweryfikowano w kodzie źródłowym i docs modułu); obraz zgodny z `docker-compose.yml`.
- `TestSupport` z `xunit.assert` — bez kolizji wersji (metapakiet `xunit` 2.9.3 i tak wciąga `xunit.assert` 2.9.3).
- Podział na 3 projekty testowe — broni się: builder używany przez oba projekty testowe, projekt testowy nie referuje drugiego projektu testowego, shared classlib to jedyny czysty szew (reuse, nie over-engineering).
- Trymowanie spójne i szczelne (walidator mierzy dokładnie to, co endpoint zapisuje; element `null` odsiany przed `Trim()` — brak NRE).
- Test kolejności listy deterministyczny (sortuje po wartościach z odpowiedzi, nie wall-clock); asertuje kontrakt (`CreatedAt`,`Id`), nie insertion order — uczciwie odnotowane.

Uwagi DROBNE (stan naniesienia w v2):

| # | Uwaga | Stan |
|---|-------|------|
| 1 | Przypiąć `Testcontainers.PostgreSql` do 4.x (starsze 3.x nie mają `PostgreSqlBuilder(string)`) i `xunit.assert` = 2.9.3 w TestSupport | ✅ naniesione (Plan kroki 6 i 8) |
| 2 | Test odrzucenia za długiego promptu nie dowodzi trymowania (trim no-op na `'x'*N`); dodać brzegowy: raw > Max, po trymie ≤ Max → brak błędu | ✅ naniesione (§ Strategia testowania) |
| 3 | Redundancja dokumentu: uzasadnienie trymowania ~5 sekcji, ścieżka błędu 400/500 ~4 sekcje | ➡ do ewentualnej kondensacji przed implementacją |

## Historia wersji

- v1 (2026-07-15): projekt system-architect + krytyka solution-critic (AKCEPTUJ Z UWAGAMI, bez blokerów); wszystkie uwagi istotne (typ zwracany walidatora, izolacja danych testów, fixture `IAsyncLifetime`) i drobne naniesione od razu w v1.
- v2 (2026-07-15): decyzje użytkownika — limity 50/8000, lista pełna z sortem z repo (`CreatedAt`+`Id`, już w pq-1), camelCase, POST `200`, CORS odłożony, trymowanie `Content` na granicy HTTP; nowe wymagania — rozdział testów unit (`*.Tests`) / integration (`*.IntegrationTests`) + wzorce testowe (builder, asercje) w shared `PromptQueue.TestSupport`. Krytyka v2: **AKCEPTUJ**; drobne (pin wersji pakietów, test brzegowy trymu) naniesione.
