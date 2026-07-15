# Raport implementacji: pq-1 — Fundament: struktura rozwiązania + baza

> Projekt: [doc/projects/pq-1.md](../projects/pq-1.md)
> Data: 2026-07-15 (6 iteracji: TDD Red→Green, poprawki CR, reorganizacja repo, uporządkowanie testów, porządki w Infrastructure, CR #2 + sprzątnięcie)
> Commit: `a0c388b`→`e037ba6` [pq-1] (fundament + reorganizacja repo) + osobny commit sprzątnięcia po CR #2
> MR: —
> Code review: 2× — iter. 1 **AKCEPTUJ Z UWAGAMI** (uwagi naniesione), iter. 6 **AKCEPTUJ** (pełny re-CR finalnego stanu: build 0/0, testy 16/16, brak dryfu migracji potwierdzony `has-pending-model-changes`; naniesiona 1 sugestia porządkowa). Iter. 2–5 bez CR — reorganizacja repo, uporządkowanie testów i przeniesienie warstwy danych do `Persistence/` (bez zmian logiki produkcyjnej).

## Co realizuje task

Fundament solution PromptQueue: struktura 4 projektów (Domain/Infrastructure/Api/Worker), model danych `Prompt` z cyklem życia (`Pending → Processing → Completed/Failed`) jako metody encji z bramkami fail-loud, persystencja EF Core 10 + Npgsql/PostgreSQL, pierwsza migracja `InitialCreate`, migracja aplikowana przez Api na starcie, minimalny szew obsługi błędów i logowania, dev-owy `docker-compose` z Postgresem. Logika HTTP (pq-2), przetwarzanie/Ollama (pq-3) i front (pq-4/pq-5) świadomie poza zakresem — Api/Worker to kompilowalne szkielety.

## Stan implementacji vs. projekt (v3)

| Decyzja projektu | Stan |
|------------------|------|
| Id = `Guid` (`Guid.NewGuid()`), generowany w konstruktorze | ✅ [Prompt.cs](../../backend/src/PromptQueue.Domain/Prompts/Prompt.cs) |
| Przejścia jako metody encji z bramkami fail-loud | ✅ `StartProcessing`/`Complete`/`Fail` + `EnsureStatus`/`EnsureNotTerminal` |
| `IPromptRepository` (Domain) + `PromptRepository` (Infra) | ✅ [IPromptRepository.cs](../../backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs), [PromptRepository.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs) |
| Enum jako string, `Id` → `ValueGeneratedNever`, `timestamptz` | ✅ [PromptConfiguration.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Configurations/PromptConfiguration.cs) |
| `GetAllAsync` → `OrderBy(CreatedAt).ThenBy(Id)` | ✅ [PromptRepository.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs) |
| Connection string tylko z env var, fail-fast | ✅ [DependencyInjection.cs](../../backend/src/PromptQueue.Infrastructure/DependencyInjection.cs), [DesignTimeDbContextFactory.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/DesignTimeDbContextFactory.cs) |
| Migracja tylko przez Api na starcie, log + fail-fast | ✅ [MigrationExtensions.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/MigrationExtensions.cs), [Program.cs](../../backend/src/PromptQueue.Api/Program.cs) |
| `GlobalExceptionHandler` (IExceptionHandler + ProblemDetails) | ✅ [GlobalExceptionHandler.cs](../../backend/src/PromptQueue.Api/GlobalExceptionHandler.cs) — w pq-1 niezweryfikowany (brak endpointu rzucającego; wg projektu) |
| Odporna pętla Workera z filtrem `when` | ✅ [PromptProcessingWorker.cs](../../backend/src/PromptQueue.Worker/PromptProcessingWorker.cs) |
| Logowanie wbudowane `Microsoft.Extensions.Logging`, poziomy w appsettings | ✅ wspólny [backend/appsettings.json](../../backend/appsettings.json) |
| docker-compose: `postgres:alpine`, healthcheck `pg_isready` | ✅ [docker-compose.yml](../../docker-compose.yml) (odstępstwo: ścieżka wolumenu — patrz Wątki) |
| Indeks na `Status`/`CreatedAt` + token współbieżności | ⏳ odłożone do pq-3 (wg projektu) |
| net10.0 | ✅ (build na SDK 10.0.110) |

## Zakres zmian

### Domain (`backend/src/PromptQueue.Domain/`)

| Plik | Op | Opis |
|------|----|------|
| [Prompts/Prompt.cs](../../backend/src/PromptQueue.Domain/Prompts/Prompt.cs) | NEW | Encja + cykl życia (Guid w ktorze, walidacja, bramki przejść) |
| [Prompts/PromptStatus.cs](../../backend/src/PromptQueue.Domain/Prompts/PromptStatus.cs) | NEW | Enum `Pending/Processing/Completed/Failed` |
| [Prompts/IPromptRepository.cs](../../backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs) | NEW | Port repozytorium (Guid) |

### Infrastructure (`backend/src/PromptQueue.Infrastructure/`)

> Dostęp do danych pod `Persistence/` (DbContext, Configurations, Repositories, Migrations, factory, `MigrationExtensions`); `DependencyInjection.cs` w rootcie.

| Plik | Op | Opis |
|------|----|------|
| [PromptQueueDbContext.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/PromptQueueDbContext.cs) | NEW | `DbContext` (primary ctor, `ApplyConfigurationsFromAssembly`) |
| [Configurations/PromptConfiguration.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Configurations/PromptConfiguration.cs) | NEW | Mapowanie encji (tabela `prompts`, enum-string, `ValueGeneratedNever`) |
| [Repositories/PromptRepository.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs) | NEW | Implementacja portu |
| [DependencyInjection.cs](../../backend/src/PromptQueue.Infrastructure/DependencyInjection.cs) | NEW | `AddInfrastructure` (DbContext + repozytorium, connection string fail-fast) |
| [DesignTimeDbContextFactory.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/DesignTimeDbContextFactory.cs) | NEW | Factory dla `dotnet ef` (env var, fail-fast) |
| [MigrationExtensions.cs](../../backend/src/PromptQueue.Infrastructure/Persistence/MigrationExtensions.cs) | NEW | `ApplyMigrationsAsync` (log + fail-fast) |
| Persistence/Migrations/20260715141018_InitialCreate.cs (+ Designer, Snapshot) | NEW | Auto-generowana migracja (tabela `prompts`) |

### Api (`backend/src/PromptQueue.Api/`)

| Plik | Op | Opis |
|------|----|------|
| [Program.cs](../../backend/src/PromptQueue.Api/Program.cs) | NEW | Host: `AddInfrastructure`, handler wyjątków, migracja na starcie, `GET /health` |
| [GlobalExceptionHandler.cs](../../backend/src/PromptQueue.Api/GlobalExceptionHandler.cs) | NEW | Globalny handler → ProblemDetails 500 + log |
| appsettings (wspólny) | MOD | Per-projekt usunięte; `Program.cs` ładuje wspólny `backend/appsettings.json` (linkowany, z `AppContext.BaseDirectory`) |

### Worker (`backend/src/PromptQueue.Worker/`)

| Plik | Op | Opis |
|------|----|------|
| [Program.cs](../../backend/src/PromptQueue.Worker/Program.cs) | NEW | Host: `AddHostedService<PromptProcessingWorker>` |
| [PromptProcessingWorker.cs](../../backend/src/PromptQueue.Worker/PromptProcessingWorker.cs) | NEW | Placeholder `BackgroundService` (log cyklu życia + odporna pętla) |
| appsettings (wspólny) | MOD | jw. — ten sam `backend/appsettings.json` |

### Orkiestracja / solution

| Plik | Op | Opis |
|------|----|------|
| [docker-compose.yml](../../docker-compose.yml) | NEW | Usługa `postgres` (dev) + healthcheck |
| backend/PromptQueue.sln | NEW | Solution + 4 projekty + testy |
| [backend/appsettings.json](../../backend/appsettings.json) | NEW | Wspólny config (logi) dla Api+Worker; linkowany do obu |
| [.gitignore](../../.gitignore) | MOD | Jeden root `.gitignore` (.NET+node+narzędzia); `launchSettings.json` odpięty od gita |
| [.vscode/settings.json](../../.vscode/settings.json) | NEW | Ukrycie `bin`/`obj` w drzewku, wskazanie solution `backend/` |

### Testy

| Plik | Op | Pokrycie |
|------|----|----------|
| [backend/tests/PromptQueue.Domain.Tests/PromptTests.cs](../../backend/tests/PromptQueue.Domain.Tests/PromptTests.cs) | NEW | 16 przypadków (konwencja `Should_…`): stan początkowy `Pending`, walidacja `Content`, przejścia happy + wszystkie nielegalne (`InvalidOperationException`). Testy generacji `Guid`/znaczników czasu świadomie pominięte (framework/trywialne, nie logika domeny) |

## Wyniki testów

| Projekt | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| PromptQueue.Domain.Tests | 16 | 0 | 0 |

`dotnet build backend/PromptQueue.sln`: **0 errors, 0 warnings**.
DoD end-to-end (Docker dostępny): `docker compose up postgres` → healthy; `dotnet ef database update` → migracja zaaplikowana; Api → logi migracji + `GET /health` = 200; środowisko posprzątane.

## Werdykt CR (po przeglądzie iter. 1)

**AKCEPTUJ Z UWAGAMI** — 0 krytycznych, 2 ostrzeżenia (naprawione w iter. 2), 3 sugestie. Implementacja wierna projektowi v3 (1:1 z blokami kodu), warstwy czyste, migracja zgodna ze schematem.

| # | Plik | Opis | Status |
|---|------|------|--------|
| O1 | [PromptTests.cs](../../backend/tests/PromptQueue.Domain.Tests/PromptTests.cs) | Brak testu `Fail()` ze stanów terminalnych (`Completed`/`Failed`) — jedyna niepokryta gałąź `throw` bramki | ✅ naprawione w iter. 2 (testy `Fail` ze stanów terminalnych; po iter. 4 jako `Should_ThrowInvalidOperation_WhenFailingFrom{Completed,Failed}`) |
| O2 | [docker-compose.yml](../../docker-compose.yml) | Floating tag `postgres:alpine` — przyszły `docker pull` może przeskoczyć major i zablokować start na istniejącym wolumenie | ✅ naprawione w iter. 2 (pin `postgres:18-alpine`, mount bez zmian) |
| S3 | [PromptQueue.Infrastructure.csproj](../../backend/src/PromptQueue.Infrastructure/PromptQueue.Infrastructure.csproj) | Jawny pin `EF Core Relational` 10.0.10 (MSB3277) | ✅ akceptowane: minimalne, udokumentowane; CPM na później |
| S4 | .gitignore | Wpisy z innego projektu (Firebird `data/*`) + duplikat `settings.local.json`. Uwaga: znalezisko CR o `KIK.Infrastructure.Web` było błędne — tego wpisu w pliku nie było | ✅ naprawione w iter. 2 (usunięto sekcję Firebird + duplikat) |
| S5 | [PromptTests.cs](../../backend/tests/PromptQueue.Domain.Tests/PromptTests.cs) | Opcjonalnie: asercja bumpu `UpdatedAt` (nadprogram) | ❌ nieprzyjęte (iter. 4) — testy znaczników czasu uznane za trywialne i usunięte, nie dodane |

## Wątki do uwagi przy CR (świadome odstępstwa)

1. **`docker-compose.yml` — wolumen pod `/var/lib/postgresql`** zamiast `/var/lib/postgresql/data`. `postgres:alpine` = dziś PG18, gdzie `PGDATA` to `/var/lib/postgresql/18/docker`, a `VOLUME` przeniesiono do parenta; montowanie parenta jest zalecane przez upstream i dane są trwałe. Reviewer potwierdził poprawność; jedyna rekomendacja to pin majora (O2).
2. **Pakiet `Microsoft.EntityFrameworkCore.Relational` 10.0.10** dodany poza 2-pakietową listą projektu — `Design` (`PrivateAssets=all`) nie propaguje wersji przez referencję projektu, więc bez jawnego pinu Api/Worker budowały się z niższą wersją tranzytywną → `MSB3277`. Rozwiązanie minimalne i udokumentowane w `.csproj`.
3. **`GlobalExceptionHandler` wchodzi niezweryfikowany** w pq-1 (brak endpointu rzucającego; w Development i tak przechwytuje `DeveloperExceptionPage`) — zgodnie z projektem, realna weryfikacja w pq-2.

## Follow-up (poza scope)

- **Handoff do pq-2**: zaktualizować przykład odpowiedzi POST w [pq-2.md](../prepare-projects/pq-2.md) na id typu `Guid` (stringi), trasa `{id:guid}`.
- **Handoff do pq-6**: Worker musi czekać na zakończenie migracji Api (nie tylko `pg_isready`).
- **Sugestia (pq-2+)**: Central Package Management (`Directory.Packages.props`), gdy przybędzie pakietów.

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | Scaffolding solution + implementacja fundamentu (Domain/Infra/Api/Worker), migracja `InitialCreate`, docker-compose, 18 testów domeny (Red→Green) | ✅ AKCEPTUJ Z UWAGAMI |
| 2 | Poprawki CR: +2 testy `Fail` ze stanów terminalnych (→20), pin `postgres:18-alpine`, sprzątnięcie `.gitignore` (Firebird + duplikat) | — |
| 3 | Reorganizacja repo: backend → `backend/` (monorepo `backend/`+`frontend/`), jeden root `.gitignore`, wspólny `backend/appsettings.json` (linkowany), `launchSettings.json` odpięty od gita, `.vscode/settings.json` | — |
| 4 | Uporządkowanie testów: usunięte 4 słabe (generacja `Guid`, znaczniki czasu), pozostałe przemianowane na konwencję `Should_…` (20 → 16, wszystkie zielone) | — |
| 5 | Porządki w Infrastructure: dostęp do danych pod `Persistence/` (namespace = folder), migracja przegenerowana; `code-backend` skill z konwencjami | — |
| 6 | CR #2 — pełny re-przegląd finalnego stanu: werdykt **AKCEPTUJ**; naniesiona sugestia porządkowa (usunięte no-opy `Property(CreatedAt/UpdatedAt)` w `PromptConfiguration`, model EF bez zmian — potwierdzone `has-pending-model-changes`) | ✅ AKCEPTUJ |
