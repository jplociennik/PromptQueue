# Raport implementacji: pq-3 — Worker + integracja z modelem językowym (Ollama)

> Projekt: [doc/projects/pq-3.md](../projects/pq-3.md)
> Data: 2026-07-15 (2 iteracje: TDD Red→Green + CR; fix O1 + test regresji)
> Commit: — (niezacommitowane; branch `pq/pq-2/main`)
> MR: —
> Code review: 1× code-reviewer (iter. 1, **AKCEPTUJ Z UWAGAMI**). Iter. 2 (fix O1) — self-review inline (zmiana 3-liniowa control-flow + test regresji), bez ponownego agenta. Build 0/0, testy 52/52 zielone (w tym Worker.IntegrationTests na Testcontainers-PostgreSQL).

## Co realizuje task

Placeholderowy `PromptProcessingWorker` (pq-1) staje się działającym silnikiem: polling promptów `Pending` (`GetByStatusAsync`), przejęcie (`StartProcessing`), wywołanie lokalnego modelu przez `IChatClient`/OllamaSharp, zapis `Completed`/`Failed`. Odporność: jedno ponowienie w pamięci, błąd/timeout → `Failed` (worker żyje), recovery przerwanych na starcie (`Requeue()`), readiness-wait aż Ollama wstanie. Bez migracji (nowa metoda domeny + query nie zmieniają schematu). Front (pq-4/pq-5) i pełny compose (pq-6) poza zakresem.

## Stan implementacji vs. projekt (v2)

| Decyzja projektu | Stan |
|------------------|------|
| Rozdział powłoka (`PromptProcessingWorker`) / logika (`PromptProcessor`), scope per cykl | ✅ [PromptProcessingWorker.cs](../../backend/src/PromptQueue.Worker/PromptProcessingWorker.cs), [PromptProcessor.cs](../../backend/src/PromptQueue.Worker/PromptProcessor.cs) |
| `IChatClient` + OllamaSharp (`llama3.2`), swap providera = 1 rejestracja | ✅ [DependencyInjection.cs](../../backend/src/PromptQueue.Worker/DependencyInjection.cs) |
| `Prompt.Requeue()` (Processing→Pending, fail-loud) | ✅ [Prompt.cs](../../backend/src/PromptQueue.Domain/Prompts/Prompt.cs) |
| `GetByStatusAsync` (filtr `Status`, sort `CreatedAt`+`Id`, `Take`) | ✅ [PromptRepository.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs) |
| Sekwencyjnie, 1 worker; bez `xmin`, bez indeksu, zero migracji | ✅ (rozstrzygnięcia parked pq-1) |
| Jedno ponowienie w pamięci (`RetryDelaySeconds`), potem `Failed` | ✅ `InvokeModelWithRetryAsync` |
| Pusta odpowiedź → `Fail` (kontrakt pq-2) | ✅ `ProcessAsync` |
| Readiness-wait na starcie (probe modelu, eskalacja WARN→ERROR po 10 próbach) | ✅ `WaitForModelAsync` |
| Config w appsettings (sekcja `Worker`), fail-fast; conn string env-only | ✅ [appsettings.json](../../backend/appsettings.json), [DependencyInjection.cs](../../backend/src/PromptQueue.Worker/DependencyInjection.cs) |
| Usługa `ollama` w dev-compose, obraz pinowany + healthcheck | ✅ [docker-compose.yml](../../docker-compose.yml) (`ollama/ollama:0.32.0`) |
| K1: jedna rejestracja hosted service | ✅ tylko w `DependencyInjection`, usunięte z `Program.cs` (zweryfikowane grepem) |

## Zakres zmian

### Domain / Infrastructure

| Plik | Op | Opis |
|------|----|------|
| [Prompts/Prompt.cs](../../backend/src/PromptQueue.Domain/Prompts/Prompt.cs) | MOD | `Requeue()` — przejście Processing→Pending z bramką |
| [Prompts/IPromptRepository.cs](../../backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs) | MOD | `GetByStatusAsync(status, maxCount, ct)` |
| [Persistence/Repositories/PromptRepository.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs) | MOD | Implementacja `GetByStatusAsync` (filtr + sort + `Take`) |

### Worker

| Plik | Op | Opis |
|------|----|------|
| [WorkerOptions.cs](../../backend/src/PromptQueue.Worker/WorkerOptions.cs) | NEW | SSOT konfiguracji (endpoint, model, interwał, batch, timeout, `RetryDelaySeconds`) |
| [PromptProcessor.cs](../../backend/src/PromptQueue.Worker/PromptProcessor.cs) | NEW | Rdzeń logiki: readiness, drain (guard per prompt), przejęcie, model z 1 ponowieniem, finalizacja, recovery |
| [PromptProcessingWorker.cs](../../backend/src/PromptQueue.Worker/PromptProcessingWorker.cs) | MOD | Placeholder → powłoka (recovery+readiness na starcie, scope per cykl, interwał z opcji) |
| [DependencyInjection.cs](../../backend/src/PromptQueue.Worker/DependencyInjection.cs) | NEW | `AddPromptProcessing` (fail-fast, `IChatClient`/Ollama singleton, procesor scoped, hosted service) |
| [Program.cs](../../backend/src/PromptQueue.Worker/Program.cs) | MOD | `AddInfrastructure` + `AddPromptProcessing`; usunięte stare `AddHostedService` (K1) |
| [PromptQueue.Worker.csproj](../../backend/src/PromptQueue.Worker/PromptQueue.Worker.csproj) | MOD | `Microsoft.Extensions.AI` 10.8.0 + `OllamaSharp` 5.4.27; `NoWarn CS9057` |

### Orkiestracja / config

| Plik | Op | Opis |
|------|----|------|
| [backend/appsettings.json](../../backend/appsettings.json) | MOD | Sekcja `Worker` (dev; Api ją ignoruje) |
| [docker-compose.yml](../../docker-compose.yml) | MOD | Usługa `ollama` (`0.32.0`, port 11434, wolumen `ollama-data`, healthcheck `ollama list`) |

### Testy

| Plik | Op | Pokrycie |
|------|----|----------|
| [TestSupport/FakeChatClient.cs](../../backend/tests/PromptQueue.TestSupport/FakeChatClient.cs) | NEW | Skryptowany `IChatClient` (`Func<int,string>`, `CallCount`, `Returning`/`Throwing`) |
| [TestSupport/PromptBuilder.cs](../../backend/tests/PromptQueue.TestSupport/PromptBuilder.cs) | NEW | Builder encji w stanach (przez metody przejść) do seedowania |
| [Worker.UnitTests/PromptProcessorTests.cs](../../backend/tests/PromptQueue.Worker.UnitTests/PromptProcessorTests.cs) + `InMemoryPromptRepository` | NEW | 10 przypadków: complete, pusta→Fail, retry-success, 2× fail, timeout≠shutdown ×2, cancel→Processing, recovery, readiness, empty queue |
| [Worker.IntegrationTests/PromptProcessorIntegrationTests.cs](../../backend/tests/PromptQueue.Worker.IntegrationTests/PromptProcessorIntegrationTests.cs) + `WorkerTestHost` | NEW | 4 przypadki na żywym Postgresie: complete+Result, fail+ErrorMessage, filtr `Status=Pending`, recovery |
| [Domain.UnitTests/PromptTests.cs](../../backend/tests/PromptQueue.Domain.UnitTests/PromptTests.cs) | MOD | +4 testy `Requeue()` (happy + 3 bramki) |
| backend/PromptQueue.sln | MOD | +2 projekty (`Worker.UnitTests`, `Worker.IntegrationTests`) |

## Wyniki testów

| Projekt | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| PromptQueue.Domain.UnitTests | 20 | 0 | 0 |
| PromptQueue.Api.UnitTests | 10 | 0 | 0 |
| PromptQueue.Api.IntegrationTests | 7 | 0 | 0 |
| PromptQueue.Worker.UnitTests | 11 | 0 | 0 |
| PromptQueue.Worker.IntegrationTests | 4 | 0 | 0 |

`dotnet build backend/PromptQueue.sln`: **0 errors, 0 warnings** (10 projektów). Worker.IntegrationTests realnie na Testcontainers (SQL `WHERE Status=…`, przejścia encji, recovery end-to-end).

## Werdykt CR (po przeglądzie iter. 1 + naprawie iter. 2)

**AKCEPTUJ** — 0 krytycznych, 0 otwartych ostrzeżeń (O1 naprawione w iter. 2), 2 sugestie (follow-up).

| # | Plik | Opis | Status |
|---|------|------|--------|
| O1 | [PromptProcessor.cs:73-114](../../backend/src/PromptQueue.Worker/PromptProcessor.cs#L73-L114) | Guard per-prompt w `ProcessPendingAsync` łyka też wyjątki `SaveChanges`/DbContext. Scenariusze (mało prawdopodobne przy 1 workerze/skali demo): padnięcie 1. `SaveChanges` → wiersz `Pending` w DB, encja `Processing` w trackerze → kolejny `GetByStatusAsync` zwraca stęchłą instancję → `StartProcessing` rzuca → guard łyka → **tight-loop** bez `Delay`. Padnięcie finalnego `SaveChanges` → **osierocenie** w `Processing` do restartu. Projekt (§ Przepływ danych) zakładał, że błąd DB bąbelkuje do `RunInScopeAsync` — guard łapie wcześniej. | ✅ naprawione w iter. 2: usunięty połykający guard → infra/DB/domenowe wyjątki bąbelkują do `RunInScopeAsync` (świeży scope w kolejnym cyklu); błędy modelu nadal łapane w `ProcessAsync`→`Fail`. Test regresji `Should_PropagateException_WhenSaveChangesFails`. Świadomie cofa K2 |
| S1 | 4× `*.csproj` testowe, 3× `NoWarn CS9057` | Central Package Management + `Directory.Build.props` w `tests/` (standing od pq-2, pq-3 amplifikuje) | ⏳ follow-up |
| S2 | [TestSupport.csproj](../../backend/tests/PromptQueue.TestSupport/PromptQueue.TestSupport.csproj) | `TestSupport → Api` ciągnie Api tranzytywnie do Worker.UnitTests (helpery potrzebują tylko Domain + M.E.AI.Abstractions) | ⏳ follow-up: rozdział TestSupport przy wzroście warstw |

## Wątki do uwagi przy CR (świadome decyzje / do dyskusji)

1. **Guard per-prompt usunięty (O1, naprawione iter. 2)** — pierwotnie guard łykał wyjątki DB → tight-loop; fix przepuszcza je do `RunInScopeAsync` (cykl kończy się, kolejny polling dostaje świeży scope). Świadomie **cofa K2** krytyka: przy pojedynczym workerze per-prompt guard nie chronił przed niczym realnym (błędy modelu i tak łapane wewnątrz `ProcessAsync`→`Fail`), a powodował O1. Udokumentowane komentarzem w [ProcessPendingAsync](../../backend/src/PromptQueue.Worker/PromptProcessor.cs#L73-L79). Pozostałość: osierocenie w `Processing`, gdy padnie finalny zapis — czyszczone przez recovery na starcie (znany limit startup-only recovery przy skali demo).
2. **Eskalacja logu WARN→ERROR po 10 próbach** ([PromptProcessor.cs:37-43](../../backend/src/PromptQueue.Worker/PromptProcessor.cs#L37-L43)) — realizacja delta-krytyki v2 #1 (odróżnienie „model się pobiera" od błędnej nazwy modelu). Reviewer potwierdził jako mandat projektu, nie odstępstwo.
3. **Healthcheck `["CMD","ollama","list"]`** ([docker-compose.yml](../../docker-compose.yml)) — daemon-up (CLI jest w obrazie, `curl` niekoniecznie); dostępność modelu weryfikuje `WaitForModelAsync`. Czysty rozdział odpowiedzialności.

## Follow-up (poza scope)

- **Manualna weryfikacja DoD z realną Ollamą** — jedyna niewykonana część: `docker compose up -d postgres ollama` → `ollama pull llama3.2` → Api (migracja) → POST prompty → Worker → obserwacja `pending→processing→completed` + ścieżka błędu. `FakeChatClient` pokrywa logikę, ale end-to-end z modelem nie był uruchomiony.
- **Central Package Management** (S1) i **rozdział TestSupport** (S2).
- **Handoff pq-6**: Worker musi czekać na migrację Api (nie tylko `pg_isready`) — `WaitForModelAsync` rozwiązuje gotowość Ollamy, ale nie schematu; mechanizm startu w compose wybiera pq-6.

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | TDD: Red (stuby `Requeue`/`GetByStatusAsync`/`PromptProcessor` + `FakeChatClient`/`PromptBuilder` + 2 projekty testowe → 18 red) → Green (pełna logika procesora z retry/readiness/recovery, powłoka, `Program.cs` K1, sekcja `Worker`, `ollama` w compose → 51/51) | ✅ AKCEPTUJ Z UWAGAMI |
| 2 | Fix O1: usunięty połykający guard per-prompt (infra/DB bąbelkuje do `RunInScopeAsync`) + test regresji `Should_PropagateException_WhenSaveChangesFails` (→ 52/52) | — (self-review inline) |
