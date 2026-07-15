# Projekt: pq-3 — Worker + integracja z modelem językowym (Ollama)

> Data: 2026-07-15
> Wersja: 2
> Werdykt: AKCEPTUJ (delta-krytyka v2 bez uwag istotnych; v1 = AKCEPTUJ Z UWAGAMI, K1–K5 naniesione)

## Cel

Zamienić placeholderowy `PromptProcessingWorker` (pq-1) w działający silnik przetwarzania: cyklicznie pobierać prompty `Pending`, wywoływać lokalny model przez Ollamę i zapisywać wynik (`Completed`) lub błąd (`Failed`), bez ubijania procesu. Źródło: [pq-3 → L16](../prepare-projects/pq-3.md#L16), [DoD → L5](../prepare-projects/DoD.md#L5). Zakres: pętla przetwarzania + integracja `IChatClient`/Ollama + usługa `ollama` w dev-compose + testy. Poza zakresem: pełny compose z workerem/frontem i README (pq-6), front (pq-4/pq-5). Worker nie migruje bazy — właścicielem schematu pozostaje Api ([pq-1 → L414](pq-1.md#L414)).

DoD: dodany prompt samoczynnie przechodzi `pending → processing → completed` z wynikiem modelu; błąd/timeout modelu → `failed` z komunikatem, a nie awaria procesu; prompt raz `Completed` nie jest przetwarzany ponownie.

## Proponowana architektura

Rozdzielenie **powłoki** (pętla, timing, zarządzanie scope) od **logiki** (gotowość modelu, przejęcie promptu, wywołanie z ponowieniem, zapis) — to jedyny nietrywialny szew testowy taska:

- `PromptProcessingWorker : BackgroundService` — singleton hosta. Na starcie: recovery + oczekiwanie na gotowość modelu; potem cykliczny polling w interwale. Na każdy cykl tworzy izolowany scope (`IServiceScopeFactory`) → świeży `DbContext`/repozytorium (BackgroundService jest singletonem, a repo jest scoped — bez scope byłby long-lived DbContext). Odporna pętla z filtrem `when` (patrz § Kluczowe decyzje).
- `PromptProcessor` — scoped, testowalna jednostka. Zależy wyłącznie od abstrakcji (`IPromptRepository`, `IChatClient`), więc nie potrzebuje własnego interfejsu (KISS, skill `code-backend` reguła 9). Cała logika: gotowość, drain kolejki (guard per prompt), przejęcie, wywołanie modelu z jednym ponowieniem, finalizacja, recovery.
- `IChatClient` (`Microsoft.Extensions.AI`) — kontrakt LLM; konkretny provider `OllamaApiClient` (pakiet **OllamaSharp**) implementuje go wprost. Rejestracja jako singleton z `HttpClient` (timeout). Wymienialny na OpenAI/Gemini przez podmianę jednej rejestracji.
- `WorkerOptions` — SSOT konfiguracji (endpoint, model, interwał, batch, timeout, backoff ponowienia), fail-fast na brak wymaganych.

Zależności: `Worker → Infrastructure` (istnieje) + `Worker → Microsoft.Extensions.AI`, `Worker → OllamaSharp`. `Api` i `Domain` nietknięte poza dwoma dodatkami niżej.

### Kluczowe abstrakcje (kod)

Domena — nowe przejście `Requeue()` (Processing→Pending) dla recovery po przerwaniu; bramka fail-loud jak reszta ([Prompt.cs](../../backend/src/PromptQueue.Domain/Prompts/Prompt.cs)):

```csharp
/// <summary>Zwraca prompt do kolejki (np. po przerwaniu przetwarzania); dozwolone wyłącznie z Processing.</summary>
public void Requeue()
{
    EnsureStatus(PromptStatus.Processing);
    Status = PromptStatus.Pending;
    Touch();
}
```

Port repozytorium — odczyt po statusie z limitem (pq-1 odłożył ten odczyt do pq-3, [pq-1 → L407](pq-1.md#L407)); dopisek do [IPromptRepository.cs](../../backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs):

```csharp
Task<IReadOnlyList<Prompt>> GetByStatusAsync(PromptStatus status, int maxCount, CancellationToken cancellationToken = default);
```

Implementacja (dopisek do [PromptRepository.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs)) — kolejność jak `GetAllAsync`:

```csharp
public async Task<IReadOnlyList<Prompt>> GetByStatusAsync(
    PromptStatus status, int maxCount, CancellationToken cancellationToken = default)
    => await dbContext.Prompts.Where(p => p.Status == status)
        .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(maxCount).ToListAsync(cancellationToken);
```

Konfiguracja workera:

```csharp
namespace PromptQueue.Worker;

/// <summary>Konfiguracja workera: endpoint i model Ollamy oraz parametry pętli. SSOT ustawień przetwarzania.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    public string OllamaBaseUrl { get; init; } = "";
    public string OllamaModel { get; init; } = "";
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 10;
    public int RequestTimeoutSeconds { get; init; } = 120;
    public int RetryDelaySeconds { get; init; } = 3;
}
```

Logika przetwarzania:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Worker;

/// <summary>Gotowość modelu, przejęcie promptu, wywołanie z jednym ponowieniem, zapis wyniku/błędu i recovery. Testowalna jednostka (scoped).</summary>
public sealed class PromptProcessor(
    IPromptRepository repository,
    IChatClient chatClient,
    WorkerOptions options,
    ILogger<PromptProcessor> logger)
{
    private const int MaxErrorLength = 2_000;

    public async Task WaitForModelAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await chatClient.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, cancellationToken);
                logger.LogInformation("Model endpoint is ready.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Model endpoint not ready ({Message}); retrying in {Delay}s.",
                    ex.Message, options.PollIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), cancellationToken);
            }
        }
    }

    public async Task RequeueInterruptedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var interrupted = await repository.GetByStatusAsync(PromptStatus.Processing, options.BatchSize, cancellationToken);
            if (interrupted.Count == 0)
                return;

            foreach (var prompt in interrupted)
                prompt.Requeue();
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pending = await repository.GetByStatusAsync(PromptStatus.Pending, options.BatchSize, cancellationToken);
            if (pending.Count == 0)
                return;

            foreach (var prompt in pending)
            {
                try
                {
                    await ProcessAsync(prompt, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error processing prompt {PromptId}; skipping.", prompt.Id);
                }
            }
        }
    }

    private async Task ProcessAsync(Prompt prompt, CancellationToken cancellationToken)
    {
        prompt.StartProcessing();
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await InvokeModelWithRetryAsync(prompt.Content, cancellationToken);
            if (string.IsNullOrWhiteSpace(result))
                prompt.Fail("Model returned an empty response.");
            else
                prompt.Complete(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model call failed for prompt {PromptId}.", prompt.Id);
            prompt.Fail(ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message);
        }

        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> InvokeModelWithRetryAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            return (await chatClient.GetResponseAsync(content, cancellationToken: cancellationToken)).Text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model call failed; retrying once in {Delay}s.", options.RetryDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), cancellationToken);
            return (await chatClient.GetResponseAsync(content, cancellationToken: cancellationToken)).Text;
        }
    }
}
```

Powłoka — zastępuje placeholder [PromptProcessingWorker.cs](../../backend/src/PromptQueue.Worker/PromptProcessingWorker.cs); jeden helper scope dla recovery, gotowości i cyklu:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PromptQueue.Worker;

/// <summary>Pętla hosta: recovery i gotowość na starcie, polling w interwale, izolowany scope na cykl. Logikę deleguje do PromptProcessor.</summary>
public sealed class PromptProcessingWorker(
    IServiceScopeFactory scopeFactory,
    WorkerOptions options,
    ILogger<PromptProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PromptProcessingWorker started.");
        await RunInScopeAsync(p => p.RequeueInterruptedAsync(stoppingToken), "startup recovery", stoppingToken);
        await RunInScopeAsync(p => p.WaitForModelAsync(stoppingToken), "model readiness wait", stoppingToken);

        var interval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunInScopeAsync(p => p.ProcessPendingAsync(stoppingToken), "processing cycle", stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("PromptProcessingWorker stopping.");
    }

    private async Task RunInScopeAsync(Func<PromptProcessor, Task> action, string phase, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<PromptProcessor>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during {Phase}; worker continues.", phase);
        }
    }
}
```

Rejestracja DI (nowy plik, wzór `AddInfrastructure`) — jedyne miejsce rejestracji hosted service (patrz § Kluczowe decyzje, K1); fail-fast na wymagane pola:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace PromptQueue.Worker;

/// <summary>Rejestracja przetwarzania: opcje (fail-fast), IChatClient (Ollama), procesor i hosted service.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPromptProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>()
            ?? throw new InvalidOperationException($"Configuration section '{WorkerOptions.SectionName}' is missing.");
        if (string.IsNullOrWhiteSpace(options.OllamaBaseUrl))
            throw new InvalidOperationException("Worker:OllamaBaseUrl is not configured.");
        if (string.IsNullOrWhiteSpace(options.OllamaModel))
            throw new InvalidOperationException("Worker:OllamaModel is not configured.");

        services.AddSingleton(options);
        services.AddSingleton<IChatClient>(_ => new OllamaApiClient(
            new HttpClient
            {
                BaseAddress = new Uri(options.OllamaBaseUrl),
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
            },
            options.OllamaModel));
        services.AddScoped<PromptProcessor>();
        services.AddHostedService<PromptProcessingWorker>();
        return services;
    }
}
```

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---|---|---|
| `Prompt.Requeue()` (Processing→Pending, bramka) | Domain (MOD) | `backend/src/PromptQueue.Domain/Prompts/Prompt.cs` |
| `IPromptRepository.GetByStatusAsync` | Domain (MOD) | `backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs` |
| `PromptRepository.GetByStatusAsync` | Infrastructure (MOD) | `backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs` |
| `WorkerOptions` | Worker (NEW) | `backend/src/PromptQueue.Worker/WorkerOptions.cs` |
| `PromptProcessor` (gotowość + drain + retry + recovery) | Worker (NEW) | `backend/src/PromptQueue.Worker/PromptProcessor.cs` |
| `PromptProcessingWorker` (zastępuje placeholder) | Worker (MOD) | `backend/src/PromptQueue.Worker/PromptProcessingWorker.cs` |
| `DependencyInjection.AddPromptProcessing` | Worker (NEW) | `backend/src/PromptQueue.Worker/DependencyInjection.cs` |
| `Program.cs` (`AddInfrastructure` + `AddPromptProcessing`; usuń stare `AddHostedService`) | Worker (MOD) | `backend/src/PromptQueue.Worker/Program.cs` |
| `.csproj` (+ `Microsoft.Extensions.AI`, `OllamaSharp`) | Worker (MOD) | `backend/src/PromptQueue.Worker/PromptQueue.Worker.csproj` |
| Sekcja `Worker` (dev: BaseUrl/Model/tunables) | Config (MOD) | `backend/appsettings.json` |
| Usługa `ollama` (pin wersji, healthcheck) | Orkiestracja (MOD) | `docker-compose.yml` |
| `FakeChatClient` (skryptowany test double `IChatClient`) | TestSupport (NEW) | `backend/tests/PromptQueue.TestSupport/FakeChatClient.cs` |
| `PromptBuilder` (encja w stanie Pending/Processing) | TestSupport (NEW) | `backend/tests/PromptQueue.TestSupport/PromptBuilder.cs` |
| `.csproj` (+ `Microsoft.Extensions.AI`) | TestSupport (MOD) | `backend/tests/PromptQueue.TestSupport/PromptQueue.TestSupport.csproj` |
| Testy `Requeue()` (happy + bramki) | Tests (unit, MOD) | `backend/tests/PromptQueue.Domain.UnitTests/PromptTests.cs` |
| `PromptProcessorTests` + `InMemoryPromptRepository` | Tests (unit, NEW) | `backend/tests/PromptQueue.Worker.UnitTests/` |
| Testy integracyjne procesora + `WorkerTestHost` | Tests (integration, NEW) | `backend/tests/PromptQueue.Worker.IntegrationTests/` |
| +2 projekty testowe do solution | — | `backend/PromptQueue.sln` |

## Przepływ danych

Encja `Prompt` jest SSOT stanu (pq-1); worker nic nie „liczy" o stanie — przejścia realizuje metodami encji, a wybór pracy filtrem `Status = Pending` w zapytaniu ([pq-1 → L389](pq-1.md#L389)). Kod przepływu w § Kluczowe abstrakcje (`PromptProcessor`) — tu tylko granice transakcji i to, czego sygnatury nie pokazują.

- **Insert (pq-2):** Api tworzy `Prompt` w `Pending`. Api tylko wstawia, worker jest jedynym piszącym przejścia — brak rywalizacji o przejścia.
- **Start workera:** `RequeueInterruptedAsync` (recovery, DB) → `WaitForModelAsync` (czeka, aż Ollama+model odpowiedzą — patrz § Kluczowe decyzje) → pętla przetwarzania.
- **Cykl:** `ProcessPendingAsync` drenuje kolejkę batchami po `BatchSize`; po opróżnieniu `Task.Delay(PollInterval)`. Per prompt (w guardzie): **przejęcie** (`StartProcessing` + `SaveChanges` — osobna transakcja, `Processing` widoczne dla pollingu frontu) → wywołanie modelu **z jednym ponowieniem** → **finalizacja** (`Complete`/`Fail` + `SaveChanges`). Pusta odpowiedź modelu → `Fail` (kontrakt „result niepuste dla completed", pq-2).
- **Recovery:** na starcie każdy `Processing` jest osierocony (pojedynczy worker właśnie wstał — nic nie jest legalnie w toku) → `Requeue()` → `Pending`. Chroni przed utknięciem po `Ctrl+C`/restarcie w trakcie inferencji.
- **Ścieżki błędu:** wyjątek modelu (nie-shutdown) → jedno ponowienie po `RetryDelaySeconds`; druga porażka → `Fail(ex.Message)` (ucięty), pętla trwa; nieoczekiwany wyjątek per prompt → złapany w `ProcessPendingAsync` (log + pominięcie promptu), cykl trwa; wyjątek DB/inny w fazie → złapany w `RunInScopeAsync` (log + kontynuacja); shutdown w trakcie inferencji → `OperationCanceledException` z `IsCancellationRequested` → rethrow → prompt zostaje `Processing` (recovery przy następnym starcie).

Delta maszyny stanów względem pq-1 (nowa krawędź powrotna; ponowienie jest w pamięci, bez przejścia stanu):

```
Pending ──StartProcessing()──► Processing ──Complete(result)──► Completed
   ▲                              │  │
   └──────────Requeue()───────────┘  └────────Fail(error)────► Failed
(Fail(error) dozwolone też z Pending)
```

## Zarządzanie stanem

- **Stan promptu** — SSOT: encja `Prompt` w bazie. Worker zmienia go wyłącznie metodami z bramkami.
- **Co jest „do zrobienia"** — SSOT: predykat `Status = Pending` w `GetByStatusAsync`; brak osobnej flagi/kolejki. Gwarancja „raz `Completed` nie jest przetwarzany ponownie" wynika z filtra.
- **Konfiguracja przetwarzania** — SSOT: `WorkerOptions` (bind z sekcji `Worker`). Interwał zastępuje twardą stałą `PollInterval` z placeholdera; nie duplikować wartości w kodzie.
- **Wynik/błąd modelu** — SSOT: `Result` / `ErrorMessage` encji.
- **Licznik prób ponowienia** — celowo NIE persystowany: jedno ponowienie żyje tylko w pamięci pojedynczego `ProcessAsync`; restart między próbami traci ten stan, ale prompt zostaje `Processing` i recovery zwraca go do kolejki (akceptowane — patrz § Kluczowe decyzje).
- **Świeżość danych** — DbContext scoped per cykl. W obrębie jednego cyklu change-tracker akumuluje encje ze wszystkich batchy (jeden scope na cykl); czyszczenie następuje między cyklami (nowy scope). Przy skali demo bez znaczenia; wyzwalacz do scope-per-batch: wzrost liczby promptów przetwarzanych w jednym cyklu.

## Kluczowe decyzje

- **Provider Ollama = pakiet OllamaSharp (`OllamaApiClient implements IChatClient`).** Zweryfikowane (2026-07): `Microsoft.Extensions.AI.Ollama` jest **deprecated** i zastąpiony przez OllamaSharp — „deprecated" dotyczy pakietu adaptera, nie modelu `llama3.2`. Abstrakcja pozostaje `IChatClient` z `Microsoft.Extensions.AI` zgodnie z CLAUDE.md. Swap na OpenAI/Gemini nadal możliwy przez podmianę jednej rejestracji w `AddPromptProcessing`, ale nie jest domyślny: Gemini/OpenAI to płatne API z kluczem, co łamie CLAUDE.md/DoD „model lokalny, bez API keys" — dlatego lokalna `llama3.2` zostaje.
- **Model domyślny: `llama3.2` (3B, ~2 GB).** Decyzja użytkownika (2026-07-15). Kompromis jakość/rozmiar/CPU, działa bez GPU; konfigurowalny przez `Worker:OllamaModel`.
- **Przetwarzanie sekwencyjne, pojedynczy worker.** Decyzja użytkownika (2026-07-15). Lokalna Ollama i tak serializuje generację per model (równoległe żądania rywalizują o GPU/CPU/VRAM bez zysku przepustowości), a równoległość wymusza token współbieżności i przejmowanie per-item — dużo pracy bez zysku. Sekwencyjnie = deterministycznie i najprościej.
- **Token współbieżności (`xmin`) — NIE dodawany** (rozstrzygnięcie parked pq-1, [pq-1 → L417](pq-1.md#L417)). Przy sekwencyjnym pojedynczym workerze niepotrzebny (Api nie robi przejść; worker jest jedynym piszącym, jeden prompt naraz). Wyzwalacz: przejście na równoległość/wiele instancji → `UseXminAsConcurrencyToken()` (kolumna systemowa, bez DDL) + przejmowanie per-item.
- **Indeks `(Status, CreatedAt)` — odłożony** (rozstrzygnięcie parked pq-1). Decyzja użytkownika (2026-07-15). Przy skali demo seq scan bez znaczenia; utrzymuje **zero migracji w pq-3** (spójne z „worker nie migruje"). Wyzwalacz: wzrost wolumenu → złożony indeks w `PromptConfiguration` + migracja aplikowana przez Api.
- **Recovery na starcie: `Processing → Pending` (`Requeue()`).** Decyzja użytkownika (2026-07-15). Nowe przejście fail-loud (tylko z `Processing`). Restart nie skazuje promptu na `Failed` — wraca do kolejki i zostaje dokończony (DoD celuje w `Completed`). Założenie: pojedynczy worker (patrz wyżej).
- **Jedno ponowienie przy błędzie modelu, potem `Failed`.** Decyzja użytkownika (2026-07-15). W `ProcessAsync`: nieudane wywołanie (nie-shutdown) → backoff `RetryDelaySeconds` (domyślnie 3 s, konfigurowalny) → jedna ponowna próba → przy drugiej porażce `Fail(error)`. Bez trwałego licznika prób (restart między próbami akceptowany — recovery i tak zwróci prompt do kolejki). Ponowienie w pamięci, bez migracji.
- **Pusta/whitespace odpowiedź modelu → `Fail("Model returned an empty response.")`, nie `Complete("")`.** Chroni kontrakt pq-2 „result niepuste dla completed". Pusta odpowiedź to udane wywołanie zwracające nic → nie podlega ponowieniu (ponowienie dotyczy wyjątków).
- **Readiness-wait na starcie: TAK, nieograniczony, logowany, tylko start.** Decyzja użytkownika (2026-07-15). `WaitForModelAsync` odpytuje `IChatClient` (minimalny probe `MaxOutputTokens = 1`) co `PollIntervalSeconds`, aż model odpowie — dzięki temu wolny boot Ollamy / pobieranie modelu nie kończy promptów `Failed` (zostają `Pending` do czasu gotowości). Probe przez `IChatClient` jest provider-agnostyczny, weryfikuje jednocześnie „Ollama wstała" i „model dostępny", oraz rozgrzewa model. Bez limitu czasu (dla „jednego polecenia up" lepiej czekać na zależność niż porzucić); anulowalny przez `stoppingToken` (shutdown przerywa). **Chroni tylko start** — awarie w trakcie obsługuje ścieżka ponowienie→`Failed` (per-cycle readiness byłby redundantny). Po dłuższym oczekiwaniu (np. > 10 prób) eskaluj poziom logu WARN→ERROR, by odróżnić „model się pobiera" od błędnej nazwy modelu w konfigu (delta-krytyka v2 #1).
- **Rozdział powłoka/logika + scope per cykl.** `BackgroundService` jest singletonem; repo/DbContext scoped → dostęp tylko przez `IServiceScopeFactory.CreateScope()`. `PromptProcessor` bez własnego interfejsu (testowalny przez wstrzyknięte abstrakcje).
- **K1: `AddPromptProcessing` jest jedynym miejscem rejestracji hosted service.** Istniejący `Program.cs` (pq-1) ma już `AddHostedService<PromptProcessingWorker>()` — musi zostać usunięty przy przepisaniu, inaczej dwie instancje workera (wyścig o `StartProcessing`, recovery resetujący cudze `Processing`).
- **Konfiguracja: fail-fast na `OllamaBaseUrl`/`OllamaModel`, tunable z defaultami.** Decyzja użytkownika (2026-07-15). Wzór connection stringa (`?? throw`, bez cichego fallbacku). `BaseUrl`/`Model` w `appsettings.json` (dev, wartości nie-sekretne) nadpisywane env w compose; connection string zostaje env-only (niesie poświadczenia). Sekcja `Worker` w wspólnym `appsettings.json` — Api ją ignoruje.
- **Image Ollamy pinowany (nie `latest`).** Lekcja CR pq-1 (O2: floating tag). Auto-pull modelu w compose to pytanie pq-6 ([pq-6 → L26](../prepare-projects/pq-6.md#L26)); w pq-3 pull udokumentowany ręcznie (`docker compose exec ollama ollama pull <model>`).
- **Test doubles pisane ręcznie (bez biblioteki mockującej).** Spójne z podejściem repo (ręczny stub w pq-2). `FakeChatClient` (skryptowany per wywołanie) + `PromptBuilder` w współdzielonym `TestSupport`; `InMemoryPromptRepository` lokalnie w `Worker.UnitTests`.

## Plan implementacji

1. Dodaj `Prompt.Requeue()` (bramka `EnsureStatus(Processing)`) — `backend/src/PromptQueue.Domain/Prompts/Prompt.cs`.
2. Dodaj `GetByStatusAsync` do portu — `backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs`.
3. Zaimplementuj `GetByStatusAsync` (filtr + sort `CreatedAt`/`Id` + `Take`) — `backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs`.
4. Dodaj pakiety `Microsoft.Extensions.AI` (stable 9.x+) i `OllamaSharp` (5.x) — `backend/src/PromptQueue.Worker/PromptQueue.Worker.csproj`.
5. Dodaj `WorkerOptions` (z `RetryDelaySeconds`) — `backend/src/PromptQueue.Worker/WorkerOptions.cs`.
6. Dodaj `PromptProcessor` (`WaitForModelAsync`, `RequeueInterruptedAsync`, `ProcessPendingAsync` z guardem per prompt, `ProcessAsync` z pustą-odpowiedzią→Fail, `InvokeModelWithRetryAsync`) — `backend/src/PromptQueue.Worker/PromptProcessor.cs`.
7. Zastąp placeholder realnym `PromptProcessingWorker` (recovery + readiness na starcie, scope per cykl, interwał z opcji) — `backend/src/PromptQueue.Worker/PromptProcessingWorker.cs`.
8. Dodaj `AddPromptProcessing` (fail-fast opcje, `IChatClient`/Ollama singleton, procesor scoped, hosted service) — `backend/src/PromptQueue.Worker/DependencyInjection.cs`.
9. Przepisz `Program.cs`: `AddInfrastructure` + `AddPromptProcessing`; **usuń istniejące `builder.Services.AddHostedService<PromptProcessingWorker>()`** (K1) — `backend/src/PromptQueue.Worker/Program.cs`.
10. Dodaj sekcję `Worker` (dev: `OllamaBaseUrl` `http://localhost:11434`, `OllamaModel` `llama3.2`, tunable) — `backend/appsettings.json`.
11. Dodaj usługę `ollama` (pin wersji, port 11434, wolumen, healthcheck) — `docker-compose.yml`.
12. Dodaj `FakeChatClient` (skryptowany, `CallCount`) i `PromptBuilder` + pakiet `Microsoft.Extensions.AI` — `backend/tests/PromptQueue.TestSupport/`.
13. Utwórz `PromptQueue.Worker.UnitTests` (ref. Worker + TestSupport) z `InMemoryPromptRepository` i testami procesora; dodaj testy `Requeue()` do `Domain.UnitTests` — `backend/tests/`.
14. Utwórz `PromptQueue.Worker.IntegrationTests` (Testcontainers + `WorkerTestHost` z `FakeChatClient`) — `backend/tests/PromptQueue.Worker.IntegrationTests/`.
15. Dodaj 2 projekty do solution — `backend/PromptQueue.sln`.
16. Weryfikacja: `. $PROFILE.CurrentUserAllHosts; all-build` (0/0), `all-test`; ręcznie: `docker compose up -d postgres ollama` + pull modelu + `dotnet run` Api (migracja) → POST prompty → `dotnet run` Worker → obserwuj readiness + `pending→processing→completed`; przypadek błędu (zatrzymaj `ollama`) → jedno ponowienie + `failed` z komunikatem, worker żyje.

## Strategia testowania

Skryptowany fake pozwala pokryć ponowienie, timeout i gotowość jednym narzędziem:

```csharp
/// <summary>Testowy IChatClient: zachowanie per wywołanie (tekst albo wyjątek), z licznikiem prób. Bez sieci.</summary>
public sealed class FakeChatClient(Func<int, string> respond) : IChatClient
{
    public int CallCount { get; private set; }

    public static FakeChatClient Returning(string text) => new(_ => text);
    public static FakeChatClient Throwing(Exception exception) => new(_ => throw exception);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(CallCount))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
```

- **Domena (`PromptQueue.Domain.UnitTests`, MOD).** `Requeue()`: `Should_TransitionToPending_WhenRequeuingFromProcessing`; bramki `Should_ThrowInvalidOperation_WhenRequeuingFrom{Pending,Completed,Failed}`.
- **Worker logika (`PromptQueue.Worker.UnitTests`, jednostkowe, `InMemoryPromptRepository` + `FakeChatClient`, `PollIntervalSeconds`/`RetryDelaySeconds` = 0).**
  - `Should_CompletePrompt_WhenModelReturnsText` (`Returning("wynik")`; `CallCount == 1`).
  - `Should_FailPrompt_WhenModelReturnsEmptyResponse` (`Returning("   ")` → `Failed`, komunikat „Model returned an empty response.", bez ponowienia; K5).
  - `Should_CompletePrompt_WhenFirstAttemptFailsAndRetrySucceeds` (`call == 1 ? throw : "wynik"` → `Completed`, `CallCount == 2`; decyzja #4).
  - `Should_FailPrompt_WhenBothAttemptsFail` (`Throwing(HttpRequestException)` → `Failed`, `CallCount == 2`).
  - `Should_FailPrompt_WhenModelTimesOutAndTokenNotCancelled` (`Throwing(TaskCanceledException)`, token niezcancelled → `Failed`, `CallCount == 2` — timeout ≠ shutdown, K3) oraz wariant timeout-then-success (`call == 1 ? throw TaskCanceledException : "wynik"` → `Completed`, K3×#4).
  - `Should_LeavePromptProcessing_WhenCancelledDuringModelCall` (`Throwing(OperationCanceledException)` + token anulowany → `ProcessPendingAsync` rethrow, prompt zostaje `Processing`).
  - `Should_RequeueInterruptedPrompts_WhenRecovering` (seed `Processing` → `Pending`).
  - `Should_ReturnWhenModelBecomesReady` (readiness, #6: `call <= 2 ? throw : "pong"` → `WaitForModelAsync` wraca, `CallCount == 3`).
  - `Should_NotCallModel_WhenQueueIsEmpty` (`CallCount == 0`).
- **Worker integracyjne (`PromptQueue.Worker.IntegrationTests`, Testcontainers `postgres:18-alpine` + realny `AddInfrastructure` + `FakeChatClient`).** Fixture `WorkerTestHost : IAsyncLifetime` (kontener, migracja przez `Database.MigrateAsync` w setupie — Api nie działa w tym harnessie; concern test-harnessu, nie naruszenie „tylko Api migruje" w runtime), buduje provider z podmienionym `IChatClient`; per test seed przez repo, resolve `PromptProcessor` w scope, wywołanie, asercja stanu w bazie: (1) `Pending` → `Completed` + `Result`; (2) `Throwing` → `Failed` + `ErrorMessage`; (3) **filtr**: seed `Completed` + `Pending` → tylko `Pending` zmienia stan (`Completed` nietknięty), weryfikuje predykat na żywym SQL; (4) recovery: `Processing` → `Pending`.
- **Manualnie (DoD, realna Ollama).** End-to-end z modelem: readiness + `pending→processing→completed`; ścieżka błędu (Ollama down / zły model) → ponowienie + `failed` z komunikatem; worker nie pada.
- **Nie testujemy:** pętli/timingu/scope powłoki (framework), wnętrza OllamaSharp/`HttpClient` (biblioteka zewnętrzna), wiązania DI (framework) — zgodnie ze skillem `code-backend`.

## Pytania do użytkownika

Brak otwartych pytań — wszystkie kwestie (model `llama3.2`, sekwencyjność, recovery `Requeue()`, jedno ponowienie, odłożony indeks/`xmin`, readiness-wait, config appsettings/env) rozstrzygnięte decyzjami użytkownika z 2026-07-15 (patrz § Kluczowe decyzje).

## Krytyka (solution-critic)

**v1 — Werdykt: AKCEPTUJ Z UWAGAMI** (zero blokerów, zero istotnych). Fakty zweryfikowane u źródeł: `Microsoft.Extensions.AI.Ollama` deprecated → OllamaSharp rekomendowany; `OllamaApiClient(HttpClient, model)` poprawny; `GetResponseAsync`/`ChatResponse.Text` = realne stable API; lifetime'y singleton/scoped OK (brak socket-exhaustion); anulowanie (rethrow shutdown vs `Fail` timeout) poprawne; zero migracji — zgoda; `WorkerTestHost.MigrateAsync` = concern test-harnessu, nie naruszenie „tylko Api migruje".

Uwagi DROBNE v1 (wszystkie naniesione w v2):

| # | Uwaga | Stan w v2 |
|---|-------|-----------|
| K1 | Stare `AddHostedService` w `Program.cs` + nowe w `AddPromptProcessing` → dwie instancje workera (wyścig o `StartProcessing`) | ✅ jawny krok 9 planu + decyzja |
| K2 | `StartProcessing`+`SaveChanges` poza try/catch — jeden feralny wpis przerywał cykl | ✅ guard per prompt w `ProcessPendingAsync` |
| K3 | Brak testu gałęzi „timeout ≠ shutdown" (TaskCanceledException bez anulowanego tokenu → `Failed`) | ✅ 2 testy (timeout→Failed, timeout→retry→Completed) |
| K4 | Opis change-trackera sugerował brak akumulacji także w cyklu | ✅ doprecyzowane w § Zarządzanie stanem |
| K5 | Pusta odpowiedź modelu → `Complete("")` łamał kontrakt pq-2 | ✅ `Fail("Model returned an empty response.")` + test |

**v2 — delta-krytyka: AKCEPTUJ** (zero blokerów/istotnych). Zweryfikowane u źródła (Microsoft Learn): overload `GetResponseAsync(string, ChatOptions?, CancellationToken)` istnieje; `IChatClient` ma dokładnie 4 składowe i `FakeChatClient` implementuje je poprawnie (`IEnumerable<ChatMessage>`, konstruktory `ChatResponse`/`ChatMessage` realne); podwójny filtr OCE w retry poprawny (shutdown → rethrow bez retry; timeout → retry; `Task.Delay` token-aware); guard per prompt spójny z rethrow anulowania. Uwagi: 1 DROBNA — nieograniczony readiness-wait maskuje błędną nazwę modelu jako wieczne czekanie → naniesiona eskalacja logu WARN→ERROR po ~10 próbach; 2 obserwacje bez akcji (retry nie odróżnia błędu trwałego od przejściowego — koszt pomijalny; teoretyczny poison-prompt niemożliwy przy pojedynczym workerze).

## Historia wersji

- v1 (2026-07-15): projekt system-architect (pętla, `Requeue()`, `GetByStatusAsync`, OllamaSharp, recovery, testy) + krytyka (AKCEPTUJ Z UWAGAMI, 5 DROBNYCH).
- v2 (2026-07-15): decyzje użytkownika — `llama3.2` (wyjaśnione nieporozumienie „deprecated"; Gemini odrzucone jako płatne API), sekwencyjnie, `Requeue()`, **jedno ponowienie w pamięci** (`RetryDelaySeconds`, bez migracji), indeks odłożony, **readiness-wait** (`WaitForModelAsync`), config w appsettings; naniesione K1–K5 krytyka.
