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

## Testy (xUnit)
- AAA z polskimi komentarzami; statyczne fabryki pomocnicze (`PendingPrompt()`/`ProcessingPrompt()`/`CompletedPrompt()`/`FailedPrompt()`).
- Asercje czasu: `>=` nie `>` (rozdzielczość zegara) — dobra praktyka, nie flaky.
- POWTARZALNE ZNALEZISKO: przy maszynie stanów sprawdzaj, czy KAŻDA gałąź `throw` bramki ma test negatywny. W pq-1 brakowało testów `Fail` ze stanów terminalnych (gałąź `EnsureNotTerminal` throw niepokryta), choć StartProcessing/Complete z terminala pokryte.

## Historia przeglądów
- pq-1 (Fundament, 2026-07-15): AKCEPTUJ Z UWAGAMI. Implementacja wierna projektowi v3. Uwagi: (1) luka testowa Fail-z-terminala, (2) pinować major tag Postgresa. Reszta (Relational pin, mount PG18, .gitignore bin/obj) OK.
