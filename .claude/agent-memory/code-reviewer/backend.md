# Backend (C# / .NET) — szczegóły

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
- Przejścia jako metody z bramkami fail-loud (`InvalidOperationException`): `StartProcessing`/`Complete`/`Fail`/`Requeue` (pq-3: Processing→Pending, bramka `EnsureStatus(Processing)`, do recovery). Stany terminalne = `Completed`/`Failed`. Enum `PromptStatus { Pending, Processing, Completed, Failed }`. Dodanie metody domenowej (bez nowych właściwości/indeksów) NIE zmienia modelu EF ⇒ ZERO migracji (potwierdzone pq-3: `Requeue`+`GetByStatusAsync` = 0 migracji, snapshot nietknięty — poprawnie).
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
- Ollama: `ollama/ollama:0.32.0` (pin, nie `latest`), port 11434, wolumen `ollama-data:/root/.ollama`, healthcheck `["CMD","ollama","list"]` (weryfikuje daemon-up; model-ready zostawiony aplikacyjnemu `WaitForModelAsync` — czysty rozdział). Model pobierany ręcznie (komentarz), auto-pull = pq-6.

## Api / warstwa HTTP (pq-2, fundament dla kolejnych endpointów)
- Minimal API = cienka warstwa aplikacji: `PromptEndpoints` (static) w feature-folderze `Api/Prompts/`, `MapGroup("/api/v1/prompts")`, handlery prywatne, DI przez parametry (bez klasy serwisu, gdy byłby pustym przelotem).
- DTO jako `record` w `PromptContracts.cs` (oddzielone od encji); mapowanie encja→DTO extension `ToResponse` w `PromptMapping.cs` (nie testować — trywialne).
- Walidacja wsadu = czysta funkcja `CreatePromptsRequestValidator.Validate(request) → Dictionary<string,string[]>` (typ wymagany przez `Results.ValidationProblem`); klucze `prompts` / `prompts[i]`; limity `public const` (SSOT). Długość liczona PO `Trim()`. Trymowanie treści TYLKO na granicy HTTP (`new Prompt(content.Trim())`) — Domain nietknięty.
- JSON: `ConfigureHttpJsonOptions` + `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`; reszta camelCase z Web defaults (dict-keys błędów NIE są camelcase'owane). POST wsadu = 200 (nie 201). Status w odpowiedzi czytany z encji (`prompts[0].Status`), nie literał — bezpieczne, bo walidacja gwarantuje niepustą listę przed dereferencją.
- Kontrakt DTO (`PromptContracts.cs`): `CreatePromptsResponse(IReadOnlyList<Guid> Ids, PromptStatus Status)`; `PromptResponse(Guid Id, string Content, PromptStatus Status, string? Result, string? ErrorMessage, DateTime CreatedAt, DateTime UpdatedAt)`. 400 = `Results.ValidationProblem(errors)` bez custom title → domyślny EN "One or more validation errors occurred." (detale w `errors`); 500 `GlobalExceptionHandler` title EN "An unexpected error occurred.".
- `public partial class Program;` na końcu `Program.cs` → dostęp dla `WebApplicationFactory<Program>`.
- Kody: 200 / 400 `ValidationProblem` / 404 (`{id:guid}` — nie-guid = trasa niedopasowana) / 500 przez `GlobalExceptionHandler`. Sort listy WYŁĄCZNIE z repo (bez duplikatu w API/froncie).
- dev-CORS (pq-4, `Api/Program.cs`): `AddCors()` + dev-only `UseCors(policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod())` PO `UseExceptionHandler`, bramkowane `IsDevelopment()` — middleware bezczynny bez nagłówka `Origin`, więc testy integracyjne (bez `Origin`; 500 w env Production) nietknięte. Prod-CORS = pq-6.

## Worker / przetwarzanie (pq-3)
- Rozdział POWŁOKA/LOGIKA: `PromptProcessingWorker : BackgroundService` (singleton) = pętla/timing/scope; `PromptProcessor` (scoped, BEZ własnego interfejsu — testowalny przez wstrzyknięte `IPromptRepository`/`IChatClient`) = cała logika. Singleton→scoped przez `IServiceScopeFactory.CreateScope()` na cykl (świeży DbContext); ZERO captured-scoped w singletonie (scopeFactory/options/logger = singletony wstrzyknięte). Egzekwuj ten wzorzec dla każdego hosted-service dotykającego DbContext.
- Port dostał `GetByStatusAsync(status, maxCount, ct)` (filtr + `OrderBy(CreatedAt).ThenBy(Id)` + `Take`, jak `GetAllAsync`). Dodanie metody do `IPromptRepository` = zaktualizuj WSZYSTKIE implementacje, w tym stuby testowe (`ThrowingPromptRepository` w Api.IntegrationTests, `InMemoryPromptRepository` w Worker.UnitTests) — mechaniczna zgodność, nie scope-creep.
- Retry modelu = JEDNO ponowienie w pamięci (bez persystencji licznika). PODWÓJNY FILTR OCE (idiom do egzekwowania): `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` PRZED `catch (Exception)` — na KAŻDYM poziomie (retry, ProcessAsync, ProcessPendingAsync). Shutdown→rethrow (prompt zostaje Processing dla recovery); timeout (`TaskCanceledException` gdy token NIE anulowany)→retry→Fail. `Task.Delay(backoff, ct)` token-aware. Pusta/whitespace odpowiedź → `Fail("Model returned an empty response.")`, NIE `Complete("")` (kontrakt pq-2), bez retry.
- Guard per prompt w `ProcessPendingAsync` (K2): `try ProcessAsync catch OCE-shutdown→rethrow / catch Exception→log+skip`, by jeden feralny wpis nie ubił batcha. LEKCJA/POWTARZALNE: pętla drenująca `while(true){ GetByStatusAsync(Pending); ... }` + guard łykający `SaveChanges`/DbException jest za szeroki — jeśli PIERWSZY save (StartProcessing) padnie a query działa, wiersz zostaje Pending w DB, encja Processing w change-trackerze (EF nie nadpisuje śledzonej) → `StartProcessing` rzuca w kółko = TIGHT-LOOP; jeśli FINALNY save padnie → prompt osierocony w Processing do restartu (recovery jest tylko na starcie). Fix zgodny z projektem: guard łapie TYLKO wyjątki modelu/domeny, a infrastruktura/DB niech bąbelkuje do `RunInScopeAsync` (cykl kończy się, następny poll = świeży scope). Niskie prawdopodobieństwo przy pojedynczym workerze/skali demo — OSTRZEŻENIE, nie bloker.
- `WaitForModelAsync` = readiness na starcie, nieograniczony, anulowalny `stoppingToken`, probe `GetResponseAsync("ping", MaxOutputTokens=1)`. Eskalacja logu WARN→ERROR po `>= EscalateAfterAttempts` (10) prób — MANDAT projektu (delta-krytyka v2 #1), nie odstępstwo. Chroni tylko start; awarie w trakcie idą ścieżką retry→Fail.
- LLM: abstrakcja `IChatClient` (`Microsoft.Extensions.AI`); provider `OllamaApiClient` (**OllamaSharp** — `Microsoft.Extensions.AI.Ollama` jest deprecated) jako singleton z `HttpClient{ BaseAddress, Timeout }`. Wywołania w kodzie przez string-overload `GetResponseAsync(string, ...)` (extension), więc `FakeChatClient` implementuje 4-składnikowy `IChatClient` (`IEnumerable<ChatMessage>`) i to wystarcza. `OllamaSharp` ciągnie source-generator pod nowszy Roslyn → `NoWarn CS9057` (udokumentowane) w Worker.csproj i obu Worker.*Tests.csproj.
- `WorkerOptions` (SSOT) bind z sekcji `Worker` w wspólnym appsettings; fail-fast w `AddPromptProcessing` (sekcja brak→throw, `OllamaBaseUrl`/`OllamaModel` puste→throw; wzór connection-stringa). BaseUrl/Model w appsettings (dev, nie-sekret), nadpisywane env w compose; conn-string zostaje env-only. `AddHostedService<PromptProcessingWorker>()` w DOKŁADNIE jednym miejscu (`AddPromptProcessing`), usunięty z Program.cs (K1). Worker Program.cs NIE woła `ApplyMigrationsAsync`.
