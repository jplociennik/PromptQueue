# Raport implementacji: pq-2 — API: dodawanie promptów + odczyt statusów

> Projekt: [doc/projects/pq-2.md](../projects/pq-2.md)
> Data: 2026-07-15 (2 iteracje: TDD Red→Green + CR; refaktor testów + CR)
> Commit: — (niezacommitowane; branch `pq/pq-2/main`)
> MR: —
> Code review: 2× — iter. 1 **AKCEPTUJ** (implementacja), iter. 2 **AKCEPTUJ** (refaktor testów). Build 0/0, testy 33/33 zielone (w tym integracyjne na Testcontainers-PostgreSQL).

## Co realizuje task

Warstwa HTTP nad fundamentem pq-1: trzy endpointy Minimal API (`POST /api/v1/prompts` wsadowo, `GET /api/v1/prompts` lista, `GET /api/v1/prompts/{id:guid}`) jako cienka warstwa nad `IPromptRepository` — bez klasy serwisu. Jedyna logika (walidacja wsadu) w czystym `CreatePromptsRequestValidator`. Task realnie weryfikuje `GlobalExceptionHandler` (500), który wszedł w pq-1 nieuruchomiony, i ustanawia konwencję testów (podział `*.UnitTests`/`*.IntegrationTests` + shared `TestSupport` z klientem/builderem/asercjami + AwesomeAssertions) jako fundament dla pq-3+. Bez zmian w Domain/Infrastructure, bez migracji. Worker/Ollama (pq-3) i front (pq-4/pq-5) poza zakresem.

## Stan implementacji vs. projekt (v2)

| Decyzja projektu | Stan |
|------------------|------|
| 3 endpointy Minimal API w `Prompts/`, bez serwisu | ✅ [PromptEndpoints.cs](../../backend/src/PromptQueue.Api/Prompts/PromptEndpoints.cs) |
| Walidator = czysta funkcja `→ Dictionary<string,string[]>` | ✅ [CreatePromptsRequestValidator.cs](../../backend/src/PromptQueue.Api/Prompts/CreatePromptsRequestValidator.cs) |
| Limity 50 / 8000; trym na granicy HTTP, długość po trymie | ✅ walidator + [PromptEndpoints.cs](../../backend/src/PromptQueue.Api/Prompts/PromptEndpoints.cs) (`new Prompt(content.Trim())`) |
| Sort listy `CreatedAt`+`Id` — z repo pq-1, zero zmian kodu | ✅ `PromptRepository.GetAllAsync` |
| JSON camelCase + enum-string; POST `200`; `{id:guid}` | ✅ [Program.cs](../../backend/src/PromptQueue.Api/Program.cs) |
| DTO oddzielone od encji (`ToResponse`) | ✅ [PromptContracts.cs](../../backend/src/PromptQueue.Api/Prompts/PromptContracts.cs), [PromptMapping.cs](../../backend/src/PromptQueue.Api/Prompts/PromptMapping.cs) |
| Weryfikacja `GlobalExceptionHandler` (env `Production` + stub → 500) | ✅ [PromptEndpointsTests.cs](../../backend/tests/PromptQueue.Api.IntegrationTests/PromptEndpointsTests.cs) |
| Rozdział testów unit/integration + shared `TestSupport` (builder + asercje) | ✅ zrealizowane; nazewnictwo doprecyzowane na `*.UnitTests` + dodany klient `PromptApiClient` i AwesomeAssertions (patrz Wątki) |
| CORS | ⏳ odłożony do pq-4/pq-6 (decyzja użytkownika) |

## Zakres zmian

### Api — warstwa HTTP (`backend/src/PromptQueue.Api/`)

| Plik | Op | Opis |
|------|----|------|
| [Prompts/PromptContracts.cs](../../backend/src/PromptQueue.Api/Prompts/PromptContracts.cs) | NEW | Rekordy DTO: `CreatePromptsRequest`, `CreatePromptsResponse`, `PromptResponse` |
| [Prompts/CreatePromptsRequestValidator.cs](../../backend/src/PromptQueue.Api/Prompts/CreatePromptsRequestValidator.cs) | NEW | Walidacja wsadu (SSOT limitów), długość po `Trim()`, klucze `prompts` / `prompts[i]` |
| [Prompts/PromptMapping.cs](../../backend/src/PromptQueue.Api/Prompts/PromptMapping.cs) | NEW | `ToResponse` (encja → DTO) |
| [Prompts/PromptEndpoints.cs](../../backend/src/PromptQueue.Api/Prompts/PromptEndpoints.cs) | NEW | `MapPromptEndpoints` + 3 handlery (DI przez parametry, batch w jednej transakcji EF) |
| [Program.cs](../../backend/src/PromptQueue.Api/Program.cs) | MOD | JSON camelCase + enum-string, `MapPromptEndpoints()`, `public partial class Program;` |

### TestSupport — wspólne wzorce testowe (nowy projekt, fundament pq-3+)

| Plik | Op | Opis |
|------|----|------|
| [PromptApiClient.cs](../../backend/tests/PromptQueue.TestSupport/PromptApiClient.cs) | NEW | Typowany klient endpointów: hermetyzuje `HttpClient` + wspólne `JsonSerializerOptions` + ścieżkę; `PostAsync`/`GetByIdAsync`/`GetAllAsync` + `ReadAsync<T>` |
| [CreatePromptsRequestBuilder.cs](../../backend/tests/PromptQueue.TestSupport/CreatePromptsRequestBuilder.cs) | NEW | Test data builder żądania (czyta limity z walidatora) |
| [ProblemDetailsAssertions.cs](../../backend/tests/PromptQueue.TestSupport/ProblemDetailsAssertions.cs) | NEW | Extension-asercje ProblemDetails (AwesomeAssertions `.Should()` w `AssertionScope`) |
| PromptQueue.TestSupport.csproj | NEW | classlib (ref. Api, `AwesomeAssertions` 9.4.0) |

### Orkiestracja / solution

| Plik | Op | Opis |
|------|----|------|
| backend/PromptQueue.sln | MOD | +3 projekty testowe pod folderem `tests`; nazwy testów jednostkowych ujednolicone do `*.UnitTests` |

### Testy

| Plik | Op | Pokrycie |
|------|----|----------|
| [PromptQueue.Api.UnitTests/CreatePromptsRequestValidatorTests.cs](../../backend/tests/PromptQueue.Api.UnitTests/CreatePromptsRequestValidatorTests.cs) | NEW | 10 przypadków walidatora (`Should_…`, AwesomeAssertions): pusta/null lista, blank (`Theory`), przekroczenie liczby, długość po trymie (+ brzegowy raw>Max/trim≤Max), poprawne, białe znaki brzegowe |
| [PromptQueue.Api.IntegrationTests/PromptApiFactory.cs](../../backend/tests/PromptQueue.Api.IntegrationTests/PromptApiFactory.cs) | NEW | Fixture `WebApplicationFactory<Program>, IAsyncLifetime` + Testcontainer `postgres:18-alpine` |
| [PromptQueue.Api.IntegrationTests/PromptEndpointsTests.cs](../../backend/tests/PromptQueue.Api.IntegrationTests/PromptEndpointsTests.cs) | NEW | 7 przypadków (klient + `AssertionScope`): happy path, trym, kolejność, 400×2, 404, 500; wspólny flow POST→GET w `PostAndFetchFirstAsync` |
| [PromptQueue.Domain.UnitTests/PromptTests.cs](../../backend/tests/PromptQueue.Domain.UnitTests/PromptTests.cs) | MOD | Rename z `Domain.Tests`; 16 przypadków przepisanych na AwesomeAssertions (`.Should().Throw<>()` itd.), intencje bez zmian |

## Wyniki testów

| Projekt | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| PromptQueue.Domain.UnitTests | 16 | 0 | 0 |
| PromptQueue.Api.UnitTests | 10 | 0 | 0 |
| PromptQueue.Api.IntegrationTests | 7 | 0 | 0 |

`dotnet build backend/PromptQueue.sln`: **0 errors, 0 warnings** (8 projektów). Integracyjne przeszły realnie (Testcontainers-PostgreSQL: migracje na starcie, 400/404/500 na żywym hoście).

## Werdykt CR

### Iter. 1 — implementacja: **AKCEPTUJ** (0 krytycznych, 0 ostrzeżeń, 4 sugestie kosmetyczne, nieprzyjęte — świadome/spójne z projektem)
Zweryfikowane edge case'y: `prompts[0].Status` przy pustej liście niemożliwe (walidacja wcześniej), szczelność trymu (brak NRE), determinizm testu kolejności, poprawny `DisposeAsync` hiding fixture.

### Iter. 2 — refaktor testów: **AKCEPTUJ** (0 krytycznych, 1 ostrzeżenie, 3 sugestie)

| # | Plik | Opis | Status |
|---|------|------|--------|
| W1 | [ProblemDetailsAssertions.cs](../../backend/tests/PromptQueue.TestSupport/ProblemDetailsAssertions.cs) | `GetProperty("errors")` rzucał przed `AssertionScope`, gdyby body nie miało `errors` (regresja) → asercje by się nie wykonały | ✅ naprawione: `TryGetProperty("errors", …)` — wszystkie asercje wykonują się także przy braku klucza |
| S1 | 4× `*.csproj` testowe | Powtórzone piny pakietów/`Using` → Central Package Management + `Directory.Build.props` w `tests/` | ⏳ follow-up (próg 4 projektów przekroczony; zrobić gdy repo urośnie) |
| S2 | [ProblemDetailsAssertions.cs](../../backend/tests/PromptQueue.TestSupport/ProblemDetailsAssertions.cs) | `using var scope` vs `using var _` (niespójność nazwy scope) | ⏳ nieprzyjęte: `scope` uzasadnione — `_` zajęte przez discard `out _` w gather JSON (inaczej CS0841) |
| S3 | [PromptApiClient.cs](../../backend/tests/PromptQueue.TestSupport/PromptApiClient.cs) | Duplikacja `JsonOptions` klient vs `Program.cs` | ⏳ nieprzyjęte: świadome — test niezależnie pinuje kontrakt wire (wykryje regresję serializacji) |

## Wątki do uwagi przy CR (świadome decyzje)

1. **AwesomeAssertions 9.4.0 zamiast xUnit `Assert`** ([TestSupport.csproj](../../backend/tests/PromptQueue.TestSupport/PromptQueue.TestSupport.csproj)) — decyzja użytkownika (2026-07-15), **odwraca** zapis z projektu v2 („odrzucone: FluentAssertions"). Powód pierwotnego odrzucenia (licencja) dotyczył FluentAssertions v8+ (komercyjna); AwesomeAssertions to darmowy fork (Apache 2.0) v7-API. Cel: wszystkie asercje na dole + `AssertionScope` (wszystkie się wykonują, widać każdą porażkę, nie stop na pierwszej). Uwaga: w 9.x namespace to `AwesomeAssertions` (nie drop-in `FluentAssertions` jak w 8.x) — świadomie wybrana najnowsza linia; alternatywa (8.2.0 z namespace `FluentAssertions`) możliwa jako swap wersji + usingów.
2. **Rename `*.Tests` → `*.UnitTests`** — decyzja użytkownika (2026-07-15) dla jawności unit vs integration w całym solution. Objął też `Domain.Tests` z pq-1 (przez `git mv`, historia zachowana). Konwencja dla pq-3+: `*.UnitTests` (bez I/O) / `*.IntegrationTests` (Testcontainers).
3. **`PromptApiClient` + `PostAndFetchFirstAsync`** ([PromptEndpointsTests.cs:119](../../backend/tests/PromptQueue.Api.IntegrationTests/PromptEndpointsTests.cs#L119)) — klient hermetyzuje `CreateClient`/`JsonOptions`/ścieżki/(de)serializację, wspólny flow POST→GET wydzielony do prywatnego helpera z rekordem `PostAndFetch` (niesie surowe odpowiedzi + DTO). Usuwa duplikację wskazaną w feedbacku; KISS — test kolejności celowo poza helperem (inny flow).
4. **DTO reużywają enuma domenowego `PromptStatus`** — świadomy, pragmatyczny wybór (enum = wartość bez zachowania). Encja `Prompt` nie wycieka przez API (mapping do `PromptResponse`) — granica DDD zachowana; zależność Api→Domain jest w poprawnym kierunku (do wewnątrz). Twardsze odseparowanie kontraktu (własny enum statusu w Api) możliwe, ale przy skali pq-2 uznane za zbędne; osobny projekt `Contracts` nieuzasadniony (jedyny konsument to front przez JSON).

## Follow-up (poza scope)

- **Central Package Management** (S1) — `Directory.Packages.props` + `Directory.Build.props` w `tests/` gdy przybędzie projektów/pakietów.
- **CORS** — dev-CORS dokłada pq-4/pq-6.
- **Handoff do pq-3**: worker filtruje `Status = Pending` w zapytaniu; `TestSupport` urośnie o `PromptBuilder` dla stanów encji; testy workera wg konwencji `Worker.UnitTests`/`Worker.IntegrationTests`.
- **Pin `Microsoft.AspNetCore.Mvc.Testing` 10.0.9** vs shared framework 10.0.10 — zrównać, gdy 10.0.10 dostępne.

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | TDD: Red (3 projekty testowe + `TestSupport` + stuby → 17 red) → Green (walidator + 3 handlery → 33/33); podział testów + builder/asercje | ✅ AKCEPTUJ |
| 2 | Refaktor testów (feedback): klient `PromptApiClient` + dedup flow POST→GET, AwesomeAssertions 9.4.0 (asercje na dole + `AssertionScope`), rename `*.Tests`→`*.UnitTests`, fix W1 | ✅ AKCEPTUJ |
