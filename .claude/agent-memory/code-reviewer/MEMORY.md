# Pamięć code-reviewer — PromptQueue

Projekt greenfield: C#/.NET 10 (`net10.0`) backend + Worker + React SPA (TS) + PostgreSQL + Ollama, orkiestracja docker-compose.
Źródło prawdy dla wierności implementacji: `doc/projects/pq-N.md` (zaakceptowany projekt techniczny). Materiały wejściowe: `doc/prepare-projects/`.

## Architektura i warstwy (egzekwuj kierunek referencji)
- `Domain` ◄ `Infrastructure` ◄ `Api`; `Infrastructure` ◄ `Worker`.
- `Domain` ma ZERO referencji (czysta domena, bez EF). `Infrastructure` zna Domain + EF/Npgsql. `Api`/`Worker` zależą tylko od `Infrastructure`.
- Namespacy: `PromptQueue.Domain.Prompts`, `PromptQueue.Infrastructure(.Configurations/.Repositories)`, `PromptQueue.Api`, `PromptQueue.Worker`.
- Worker referuje Infrastructure, ale w pq-1 jeszcze go nie używa (AddInfrastructure dokłada pq-3) — świadome, nie flaguj.

## Konwencje kodu (potwierdzone)
- Identyfikatory po angielsku; `<summary>` po polsku (jednolinijkowe, cel biznesowy) na publicznych typach/metodach.
- Primary Constructor dla DI, BEZ prywatnych pól na wstrzyknięte zależności (np. `PromptRepository(PromptQueueDbContext dbContext)`, `GlobalExceptionHandler(ILogger, IProblemDetailsService)`).
- `Nullable` enable + `ImplicitUsings` enable we wszystkich projektach.

## Wzorce domeny
- Encja `Prompt`: enkapsulacja, prywatne settery, prywatny ktor bezparam. dla EF. `Id` = `Guid` (`Guid.NewGuid()`, v4) nadawany w konstruktorze — encja SSOT własnego Id, stanu i czasów (`DateTime.UtcNow`).
- Przejścia jako metody z bramkami fail-loud (`InvalidOperationException`): `StartProcessing`/`Complete`/`Fail`. Stany terminalne = `Completed`/`Failed`. Enum `PromptStatus { Pending, Processing, Completed, Failed }`.
- Port `IPromptRepository` w Domain; implementacja w Infrastructure. `GetAllAsync` sort `OrderBy(CreatedAt).ThenBy(Id)` (Guid v4 nie niesie porządku → tie-break po Id).

## EF Core / migracje / dane
- `PromptConfiguration`: `ValueGeneratedNever()` na Id, `Status` `HasConversion<string>().HasMaxLength(20)`, `ApplyConfigurationsFromAssembly`. Tabela `prompts`.
- Npgsql: `Guid`→`uuid`, UTC `DateTime`→`timestamptz` (`timestamp with time zone`), string→`text`.
- Snapshot/Designer w 100% z automatu. UWAGA: Npgsql zawsze emituje `NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder)` na poziomie modelu — to NORMALNE (default strategii), nie ręczna edycja i nie kłóci się z `ValueGeneratedNever` na kluczu Guid. Zmiana modelu ⇒ nowa migracja + snapshot w zgodzie.
- Connection string: WYŁĄCZNIE env var `ConnectionStrings__PromptQueue`, fail-fast bez fallbacku (SSOT). NIE w appsettings. Jeden mechanizm w Api, Worker i `DesignTimeDbContextFactory`.
- Migracje aplikuje TYLKO Api na starcie (`ApplyMigrationsAsync`: log + rethrow/fail-fast). Worker nigdy nie migruje.

## Pakiety / build
- Infrastructure jawnie pina `Microsoft.EntityFrameworkCore.Relational` do wersji Design (`PrivateAssets=all` nie propaguje wersji przez referencję projektu) → usuwa `MSB3277`. Udokumentowane komentarzem. Gdy repo urośnie, rozważ Central Package Management + transitive pinning.

## docker-compose
- Postgres: wolumen montowany na `/var/lib/postgresql` (layout PG18: PGDATA=`/var/lib/postgresql/18/docker`; VOLUME przeniesiony do parenta). Mount parenta jest poprawny i trwały (dane nested pod mountem). REKOMENDACJA: pinuj major tag (`postgres:18-alpine`) zamiast floating `postgres:alpine` — future major bump inaczej cicho psuje wolumen dev.
- Hasło Postgresa w compose dla dev — akceptowalne na tym etapie (tylko lokalny Postgres).

## Api / warstwa HTTP (pq-2, fundament dla kolejnych endpointów)
- Minimal API = cienka warstwa aplikacji: `PromptEndpoints` (static) w feature-folderze `Api/Prompts/`, `MapGroup("/api/v1/prompts")`, handlery prywatne, DI przez parametry (bez klasy serwisu, gdy byłby pustym przelotem).
- DTO jako `record` w `PromptContracts.cs` (oddzielone od encji); mapowanie encja→DTO extension `ToResponse` w `PromptMapping.cs` (nie testować — trywialne).
- Walidacja wsadu = czysta funkcja `CreatePromptsRequestValidator.Validate(request) → Dictionary<string,string[]>` (typ wymagany przez `Results.ValidationProblem`); klucze `prompts` / `prompts[i]`; limity `public const` (SSOT). Długość liczona PO `Trim()`. Trymowanie treści TYLKO na granicy HTTP (`new Prompt(content.Trim())`) — Domain nietknięty.
- JSON: `ConfigureHttpJsonOptions` + `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`; reszta camelCase z Web defaults (dict-keys błędów NIE są camelcase'owane). POST wsadu = 200 (nie 201). Status w odpowiedzi czytany z encji (`prompts[0].Status`), nie literał — bezpieczne, bo walidacja gwarantuje niepustą listę przed dereferencją.
- `public partial class Program;` na końcu `Program.cs` → dostęp dla `WebApplicationFactory<Program>`.
- Kody: 200 / 400 `ValidationProblem` / 404 (`{id:guid}` — nie-guid = trasa niedopasowana) / 500 przez `GlobalExceptionHandler`. Sort listy WYŁĄCZNIE z repo (bez duplikatu w API/froncie).

## Testy (xUnit)
- AAA z polskimi komentarzami; statyczne fabryki pomocnicze (`PendingPrompt()`/`ProcessingPrompt()`/`CompletedPrompt()`/`FailedPrompt()`). W testach API buildery zamiast fabryk (niżej).
- Asercje czasu: `>=` nie `>` (rozdzielczość zegara) — dobra praktyka, nie flaky.
- POWTARZALNE ZNALEZISKO: przy maszynie stanów sprawdzaj, czy KAŻDA gałąź `throw` bramki ma test negatywny. W pq-1 brakowało testów `Fail` ze stanów terminalnych (gałąź `EnsureNotTerminal`) — ZAMKNIĘTE w pq-2 (`Should_ThrowInvalidOperation_WhenFailingFromCompleted/FromFailed` istnieją; Domain = 16 przypadków).
- STRUKTURA (konwencja od pq-2): jednostkowe = `*.UnitTests` (bez I/O; `PromptQueue.Domain.UnitTests`, `PromptQueue.Api.UnitTests`), integracyjne = `*.IntegrationTests` (Testcontainers/Docker). Sufiks `*.Tests` już NIE używany. Shared classlib `PromptQueue.TestSupport` (zwykły classlib, `IsPackable=false`, BEZ Test SDK/xunit) na buildery/klienta/asercje dla OBU typów; test-projekt referuje TYLKO TestSupport, nie inny test-projekt.
- Biblioteka asercji: **AwesomeAssertions 9.4.0** (darmowy fork FluentAssertions v7; przyjęty w refaktorze pq-2 — wcześniejsze „odrzucenie FluentAssertions" dotyczyło płatnej licencji v8, nie API). Namespacy `AwesomeAssertions` + `AwesomeAssertions.Execution`; global `<Using>` per projekt testowy (NIE propaguje się przez ProjectReference — deklaruj w każdym). Idiomy: `.Should().Be/HaveCount/ContainKey/BeEmpty/Equal/BeTrue/Throw<>()`. `[Fact]/[Theory]` zostają z xUnit (runner). Odrzucone nadal: AutoFixture (niedeterminizm), ObjectMother.
- **AssertionScope** (`using var _ = new AssertionScope();`): asercje NA DOLE testu; >1 asercja → w scope (wykonują się wszystkie, nie stop na pierwszej); 1 asercja → bez scope. PUŁAPKA async: `await`/materializuj wartości PRZED otwarciem scope; wewnątrz scope tylko synchroniczne `.Should()`. Helpery (`ProblemDetailsAssertions`) trzymają scope u siebie. UWAGA: gather PRZED scope nie może rzucać (np. `JsonElement.GetProperty("x")` → użyj `TryGetProperty`), inaczej wywali się przed wykonaniem asercji w scope.
- Wzorce TestSupport: `CreatePromptsRequestBuilder` (fluent, czyta stałe walidatora), `ProblemDetailsAssertions` (extension na `HttpResponseMessage`: `ShouldBeValidationProblemAsync`/`ShouldBeProblemAsync`), `PromptApiClient` (typowany klient: opakowuje `HttpClient` + wspólne `JsonSerializerOptions` camelCase+enum-string + ścieżki; metody zwracają surowy `HttpResponseMessage` + osobne `ReadAsync<T>` → status/nagłówki widoczne do asercji). Dedup flow POST→GET-po-id przez prywatny `record PostAndFetch` w klasie testu. JsonOptions świadomie zduplikowany vs Program.cs (test pinuje kontrakt wire).
- Integracja API: `PromptApiFactory : WebApplicationFactory<Program>, IAsyncLifetime`; kontener Postgres w `InitializeAsync` PRZED budową hosta (fail-fast conn-stringa), conn-string przez `UseSetting("ConnectionStrings:PromptQueue", _postgres.GetConnectionString())` w `ConfigureWebHost`. Sprzątanie: `public new async Task DisposeAsync()` (hides base `ValueTask`) — najpierw `await base.DisposeAsync()`, potem kontener; POPRAWNY, akceptowany idiom (NIE flaguj). Testcontainers.PostgreSql 4.x → `new PostgreSqlBuilder("postgres:18-alpine")`.
- Izolacja bez resetu bazy: kontener współdzielony w klasie (`IClassFixture`), asercje na WŁASNYCH `Id` (filtr po ids z odpowiedzi). Test kolejności: posortuj podzbiór po (CreatedAt,Id) i porównaj z kolejnością odpowiedzi — deterministyczne, bez zegara.
- 500/`GlobalExceptionHandler`: `WithWebHostBuilder`→`UseEnvironment("Production")` (omija DeveloperExceptionPage) + `ConfigureTestServices`(`RemoveAll<IPromptRepository>()` + stub rzucający). Kontener musi żyć też tutaj — migracja na starcie hosta idzie realnym DbContext, nie stubem.

## Historia przeglądów
- pq-1 (Fundament, 2026-07-15): AKCEPTUJ Z UWAGAMI. Implementacja wierna projektowi v3. Uwagi: (1) luka testowa Fail-z-terminala, (2) pinować major tag Postgresa. Reszta (Relational pin, mount PG18, .gitignore bin/obj) OK.
- pq-2 (API promptów + odczyt statusów, 2026-07-15): AKCEPTUJ. Wierna zaakceptowanemu projektowi v2 (13 elementów, ścieżki, piny pakietów). Build 0/0, 33/33 (Domain 16 + Api.UnitTests 10 + Api.IntegrationTests 7); data-layer nietknięty (0 migracji, słusznie). Zero uwag krytycznych; sugestie kosmetyczne: walidator nie short-circuituje przy nadmiarowej liczbie (pełny loop, nieszkodliwe przy limicie body), brak `<summary>` na publicznych helperach TestSupport (self-explanatory). Zweryfikowane bezpieczne: `prompts[0].Status`, DisposeAsync-hiding, determinizm testu kolejności, ścieżka 500.
- pq-2 refaktor testów (follow-up, 2026-07-15): AKCEPTUJ. Warstwa testów + rename `*.Tests`→`*.UnitTests` + AwesomeAssertions 9.4.0; produkcja nietknięta poza fazą GREEN w `Program.cs` (JSON enum converter, `MapPromptEndpoints`, `partial Program`). Dedup przez `PromptApiClient` + `record PostAndFetch` — realny, KISS, nie chowa statusu/nagłówków. AssertionScope poprawny (await przed scope, single-assert bez scope). 33/33 zachowane, `ThrowingPromptRepository`/500 OK, `.sln` bez martwych wpisów. 1 drobne OSTRZEŻENIE: `ProblemDetailsAssertions.cs:13` `GetProperty("errors")` rzuca przed scope → `TryGetProperty`. SUGESTIA: CPM (Directory.Packages.props) — 4 projekty testowe dublują te same piny.
