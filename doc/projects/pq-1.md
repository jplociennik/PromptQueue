# Projekt: pq-1 — Fundament: struktura rozwiązania + baza

> Data: 2026-07-15
> Wersja: 3
> Werdykt: AKCEPTUJ Z UWAGAMI (uwagi krytyki naniesione w v3)

## Cel

Położyć fundament, na którym stają pq-2…pq-6: strukturę solution, model danych (`Prompt` + cykl życia) i persystencję (EF Core/Npgsql/PostgreSQL). Zakres pq-1 to **struktura + baza + minimalny szew hostów** (obsługa błędów i logowanie na poziomie hosta, nie logika HTTP/przetwarzania), bez endpointów (pq-2), workera/Ollamy (pq-3) i frontu (pq-4/pq-5). DoD tasku: rozwiązanie się kompiluje, migracja tworzy schemat, `docker compose up` stawia Postgres, aplikacja łączy się z bazą ([pq-1 → L22](../prepare-projects/pq-1.md#L22)).

## Proponowana architektura

Cztery projekty pod `backend/src/`, warstwy lekkie (bez ciężkiego DDD). Domena jest czysta (zero zależności od EF), Infrastructure zna Domenę i EF, procesy (Api/Worker) zależą tylko od Infrastructure.

```
Domain  ◄──  Infrastructure  ◄──  Api      (ASP.NET Core, minimal API)
                    ▲
                    └──────────────  Worker   (Generic Host + BackgroundService)
```

- `PromptQueue.Domain` — encja `Prompt`, enum `PromptStatus`, port `IPromptRepository`. SSOT reguł przejść stanu. Brak referencji.
- `PromptQueue.Infrastructure` — `PromptQueueDbContext`, `PromptRepository`, konfiguracja encji, migracje, `MigrationExtensions`, rejestracja DI, factory dla narzędzi EF. Referencja → Domain.
- `PromptQueue.Api` — szkielet hosta: `AddInfrastructure`, migracja na starcie (log + fail-fast), globalny handler wyjątków, `GET /health`. Endpointy domyka pq-2.
- `PromptQueue.Worker` — szkielet hosta z placeholderowym `BackgroundService` (logowanie cyklu życia + odporna pętla). Pętlę przetwarzania domyka pq-3.

### Kluczowe abstrakcje (kod)

Encja — enkapsulacja, przejścia jako metody z bramkami (nie settery). `Id` typu `Guid` nadawany w konstruktorze — encja jest SSOT także własnego identyfikatora:

```csharp
namespace PromptQueue.Domain.Prompts;

public enum PromptStatus { Pending, Processing, Completed, Failed }
```

```csharp
namespace PromptQueue.Domain.Prompts;

/// <summary>Zgłoszony prompt i jego cykl życia; SSOT reguł przejść stanu i własnego Id.</summary>
public class Prompt
{
    private Prompt() { }

    public Prompt(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));

        Id = Guid.NewGuid();
        Content = content;
        Status = PromptStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public string Content { get; private set; } = null!;
    public PromptStatus Status { get; private set; }
    public string? Result { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void StartProcessing()
    {
        EnsureStatus(PromptStatus.Pending);
        Status = PromptStatus.Processing;
        Touch();
    }

    public void Complete(string result)
    {
        EnsureStatus(PromptStatus.Processing);
        Result = result;
        Status = PromptStatus.Completed;
        Touch();
    }

    public void Fail(string error)
    {
        EnsureNotTerminal();
        ErrorMessage = error;
        Status = PromptStatus.Failed;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private void EnsureStatus(PromptStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Expected {expected} but was {Status}.");
    }

    private void EnsureNotTerminal()
    {
        if (Status is PromptStatus.Completed or PromptStatus.Failed)
            throw new InvalidOperationException($"Prompt is in terminal state {Status}.");
    }
}
```

Port repozytorium w Domenie — minimalny (pq-3 doda odczyt `Pending` w swoim zakresie, patrz § Kluczowe decyzje):

```csharp
namespace PromptQueue.Domain.Prompts;

public interface IPromptRepository
{
    void Add(Prompt prompt);
    Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Prompt>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

`DbContext` — konwencja `ApplyConfigurationsFromAssembly`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace PromptQueue.Infrastructure;

public class PromptQueueDbContext(DbContextOptions<PromptQueueDbContext> options) : DbContext(options)
{
    public DbSet<Prompt> Prompts => Set<Prompt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromptQueueDbContext).Assembly);
}
```

Konfiguracja encji — `Id` generowany przez aplikację (`ValueGeneratedNever`), enum jako string; Npgsql mapuje `Guid` → `uuid`, `DateTime` (UTC) → `timestamptz`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Infrastructure.Configurations;

public class PromptConfiguration : IEntityTypeConfiguration<Prompt>
{
    public void Configure(EntityTypeBuilder<Prompt> builder)
    {
        builder.ToTable("prompts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Content).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);
    }
}
```

Repozytorium — implementacja portu; `GetAllAsync` z deterministycznym porządkiem (`CreatedAt`, tie-break `Id`, patrz § Kluczowe decyzje):

```csharp
using Microsoft.EntityFrameworkCore;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Infrastructure.Repositories;

public class PromptRepository(PromptQueueDbContext dbContext) : IPromptRepository
{
    public void Add(Prompt prompt) => dbContext.Prompts.Add(prompt);

    public Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Prompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Prompt>> GetAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.Prompts.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
```

Rejestracja DI — connection string fail-fast z env var (patrz § Kluczowe decyzje):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptQueue.Domain.Prompts;
using PromptQueue.Infrastructure.Repositories;

namespace PromptQueue.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PromptQueue")
            ?? throw new InvalidOperationException("Connection string 'PromptQueue' is not configured.");

        services.AddDbContext<PromptQueueDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IPromptRepository, PromptRepository>();
        return services;
    }
}
```

Factory design-time — pozwala `dotnet ef` działać bez uruchamiania hosta; czyta env var (fail-fast):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PromptQueue.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PromptQueueDbContext>
{
    public PromptQueueDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PromptQueue")
            ?? throw new InvalidOperationException("Environment variable 'ConnectionStrings__PromptQueue' is not set.");

        var options = new DbContextOptionsBuilder<PromptQueueDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PromptQueueDbContext(options);
    }
}
```

### Szew obsługi błędów i logowania (hosty)

Migracja na starcie Api — log informacyjny i fail-fast (rethrow) przy błędzie; jedyna realna ścieżka błędu w pq-1:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PromptQueue.Infrastructure;

public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PromptQueueDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PromptQueueDbContext>>();

        try
        {
            logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migrations applied.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply database migrations.");
            throw;
        }
    }
}
```

Globalny handler wyjątków Api — szkielet/szew, z którego skorzystają endpointy pq-2 (patrz § Kluczowe decyzje):

```csharp
using Microsoft.AspNetCore.Diagnostics;

namespace PromptQueue.Api;

/// <summary>Ostatnia linia obrony potoku HTTP: loguje wyjątek, zwraca ProblemDetails (500).</summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}.",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Title = "An unexpected error occurred." }
        });
    }
}
```

Podłączenie w hoście Api (logowanie konsolowe domyślne w `WebApplication.CreateBuilder`):

```csharp
using PromptQueue.Api;
using PromptQueue.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

await app.Services.ApplyMigrationsAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
```

Placeholder Workera — logowanie cyklu życia + try/catch wokół ciała pętli (wyjątek nie ubija procesu); pełna pętla w pq-3:

```csharp
namespace PromptQueue.Worker;

/// <summary>Odporny szkielet pętli przetwarzania; pełna logika w pq-3.</summary>
public sealed class PromptProcessingWorker(ILogger<PromptProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PromptProcessingWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in processing loop; worker continues.");
            }
        }

        logger.LogInformation("PromptProcessingWorker stopping.");
    }
}
```

Host Workera (`Host.CreateApplicationBuilder`) rejestruje `PromptProcessingWorker` jako hosted service i nie aplikuje migracji; w pq-1 nie sięga do bazy (`AddInfrastructure` dołoży pq-3). Poziomy logowania w `appsettings.json` obu hostów:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---|---|---|
| `PromptStatus` (enum) | Domain | `backend/src/PromptQueue.Domain/Prompts/PromptStatus.cs` |
| `Prompt` (encja, `Guid` w konstruktorze + przejścia) | Domain | `backend/src/PromptQueue.Domain/Prompts/Prompt.cs` |
| `IPromptRepository` (port, `Guid`) | Domain | `backend/src/PromptQueue.Domain/Prompts/IPromptRepository.cs` |
| `PromptQueueDbContext` | Infrastructure | `backend/src/PromptQueue.Infrastructure/Persistence/PromptQueueDbContext.cs` |
| `PromptConfiguration` (`Id` → `ValueGeneratedNever`) | Infrastructure | `backend/src/PromptQueue.Infrastructure/Persistence/Configurations/PromptConfiguration.cs` |
| `PromptRepository` (`Guid`, sort `CreatedAt`+`Id`) | Infrastructure | `backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs` |
| `AddInfrastructure` (DI) | Infrastructure | `backend/src/PromptQueue.Infrastructure/DependencyInjection.cs` |
| `DesignTimeDbContextFactory` | Infrastructure | `backend/src/PromptQueue.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` |
| `MigrationExtensions.ApplyMigrationsAsync` (log + fail-fast) | Infrastructure | `backend/src/PromptQueue.Infrastructure/Persistence/MigrationExtensions.cs` |
| Migracja `InitialCreate` (`uuid` PK) | Infrastructure | `backend/src/PromptQueue.Infrastructure/Persistence/Migrations/*` (generowane) |
| Host + `/health` + migracja na starcie | Api | `backend/src/PromptQueue.Api/Program.cs`, `appsettings*.json` |
| `GlobalExceptionHandler` + rejestracja | Api | `backend/src/PromptQueue.Api/GlobalExceptionHandler.cs`, `Program.cs` |
| Host + placeholder `PromptProcessingWorker` (log + try/catch) | Worker | `backend/src/PromptQueue.Worker/Program.cs`, `PromptProcessingWorker.cs` |
| Usługa `postgres` (dev) | Orkiestracja | `docker-compose.yml` |
| Solution + referencje projektów | — | `backend/PromptQueue.sln` |
| Testy jednostkowe domeny | Tests | `backend/tests/PromptQueue.Domain.Tests/PromptTests.cs` |

## Przepływ danych

Encja jest SSOT swojego stanu i Id; `IPromptRepository` to jedyna brama persystencji dla procesów. W pq-1 nie ma jeszcze wywołujących — poniższy przepływ opisuje kontrakt, którego użyją pq-2 (insert) i pq-3 (przejścia):

- **Zapis (pq-2):** `new Prompt(content)` nadaje `Id` (`Guid.NewGuid()`) i `Pending` w konstruktorze → `Id` znany **przed** persystencją (przydatne do korelacji/logów/optimistic UI) → `IPromptRepository.Add` → `SaveChangesAsync` → Npgsql `INSERT` z tym `uuid`.
- **Przejścia (pq-3):** wczytanie `Prompt` przez repozytorium → `StartProcessing()` / `Complete(result)` / `Fail(error)` → `SaveChangesAsync`. Bramki metod to **strażniki fail-loud** — nielegalne przejście (w tym ze stanu terminalnego) rzuca `InvalidOperationException`. Gwarancję „raz `Completed` nie jest przetwarzany ponownie" ([pq-3 → L17](../prepare-projects/pq-3.md#L17)) realizuje pq-3 **filtrem `Status = Pending` w zapytaniu workera**, nie ponownym wywołaniem metod przejść.

Ścieżki błędu (szew hostów z pq-1): błąd migracji (start Api) → `LogError` + rethrow → host nie wstaje (fail-fast); nieobsłużony wyjątek Api (pq-2+) → `GlobalExceptionHandler` → `ProblemDetails` 500; wyjątek w pętli Workera (pq-3+) → złapany, zalogowany, pętla kontynuuje.

Dozwolone krawędzie stanu (to, czego sygnatury metod nie pokazują):

```
Pending ──StartProcessing()──► Processing ──Complete(result)──► Completed
   │                               │
   └───────────Fail(error)─────────┴───────────────► Failed
```

## Kluczowe decyzje

- **Id = `Guid` (`Guid.NewGuid()`, wariant v4), generowany w konstruktorze.** Decyzja użytkownika (2026-07-15; zmiana z `int identity` z v2, wybór `NewGuid()` potwierdzony wprost). Driver: preferencja użytkownika + `Id` znany po stronie aplikacji **przed** zapisem (korelacja/logi). Koszt v4 — losowy klucz → fragmentacja indeksu PK — pomijalny przy skali demo (rozważany wariant v7 z uporządkowaniem czasowym odrzucony przez użytkownika). `PromptConfiguration` pina `ValueGeneratedNever()`; Npgsql mapuje `Guid`→`uuid`. Rozstrzyga otwartą kwestię #1.
- **Kolejność `GetAllAsync`: `OrderBy(CreatedAt).ThenBy(Id)`.** `Id` (losowy Guid) nie niesie porządku wstawiania, więc sortujemy chronologicznie po `CreatedAt`; `ThenBy(Id)` daje deterministyczny tie-break przy równych znacznikach (możliwe w batch-POST z pq-2, gdzie wiele encji dostaje ten sam `UtcNow`) — bez tego kolejność listy we froncie (pq-5, polling) mogłaby „skakać" między odświeżeniami. Indeks pod ten sort odłożony do pq-3 (skala demo).
- **Handoff do pq-2: przykład odpowiedzi POST wymaga aktualizacji na `Guid`.** [pq-2 → L16](../prepare-projects/pq-2.md#L16) pokazuje `{ "ids": [1, 2], "status": "pending" }` (liczby) — po zmianie na `Guid` id serializują się jako stringi, a trasa `GET /api/v1/prompts/{id}` powinna użyć wiązania `{id:guid}`. `pq-2.md` **nie jest edytowany w tym tasku** — domknie to `/design pq-2`. (Front pq-4/pq-5 nie zakłada typu id — `key` z backendu działa i dla stringa.)
- **Tylko `Content`, bez nazwy/tytułu.** DoD, pq-2, pq-4 i pq-5 konsekwentnie mówią wyłącznie o treści promptu — pole tytułu byłoby funkcją „na zapas". Rozstrzyga #2.
- **Repozytorium: `IPromptRepository` (port w Domain) + `PromptRepository` (Infrastructure).** Decyzja użytkownika (2026-07-15) — wyraźny szew testowy; procesy nie dotykają `DbContext` bezpośrednio. Zgodne wprost z [pq-2 → L26](../prepare-projects/pq-2.md#L26) („cienka warstwa aplikacji nad repozytorium"). Interfejs minimalny; odczyt `Pending` doda pq-3. Rozstrzyga #3.
- **pq-1 tworzy wszystkie 4 projekty.** Domain/Infrastructure realne, Api/Worker jako minimalne kompilowalne szkielety — „rozwiązanie się kompiluje" i „aplikacja łączy się z bazą" są demonstrowalne już teraz, a pq-2/pq-3 dokładają wyłącznie logikę. Rozstrzyga #4.
- **.NET 10 (LTS), `net10.0`.** Aktualny LTS; EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x. Potwierdzone na maszynie: `dotnet --list-sdks` → SDK 10.0.109.
- **Enum jako string w bazie** (`HasConversion<string>()`). Czytelny podgląd kolejki podczas demo; odporny na zmianę kolejności wartości enuma.
- **Znaczniki czasu w encji** (`DateTime.UtcNow` w konstruktorze i `Touch()`) mapowane na `timestamptz` (Npgsql wymaga UTC). Encja SSOT własnego stanu, w tym czasu aktualizacji.
- **Logowanie: wbudowane `Microsoft.Extensions.Logging` (konsola), bez Serilog.** Decyzja użytkownika (2026-07-15). Zero dodatkowych pakietów; provider konsolowy domyślny w hostach Api (`WebApplication.CreateBuilder`) i Worker (`Host.CreateApplicationBuilder`), widoczny w `docker compose logs`; jedno API `ILogger` w obu procesach. Poziomy w `appsettings`: `Default: Information`, `Microsoft.EntityFrameworkCore: Warning` (bez zalewu logami SQL — logi migracji idą przez `ILogger<PromptQueueDbContext>`, więc pozostają widoczne). Serilog / structured sink to opcjonalny późniejszy swap w jednym punkcie rejestracji.
- **`GlobalExceptionHandler` to infrastruktura hosta, nie logika HTTP.** `IExceptionHandler` + `ProblemDetails` to szew na końcu potoku ASP.NET Core, niezależny od endpointów — mieści się w „szkielet hosta Api". `AddProblemDetails()` + `IProblemDetailsService` respektuje negocjację treści. **Uwaga:** w pq-1 nie ma endpointu, który rzuca, a w środowisku Development `WebApplication` wstawia `DeveloperExceptionPage` przed handlerem — więc ścieżka `ProblemDetails` 500 wchodzi w pq-1 **niezweryfikowana**; realnie zweryfikuje ją pq-2 (endpoint zdolny rzucić, uruchomienie poza Development).
- **Migracje aplikuje wyłącznie Api na starcie (`ApplyMigrationsAsync`), z logiem i fail-fast.** Jeden właściciel schematu — eliminuje wyścig migracji przy równoległym starcie w [pq-6 → L16](../prepare-projects/pq-6.md#L16); przy wyjątku `LogError` + rethrow (host nie wstaje na niesprawnej bazie). Worker nigdy nie migruje.
- **Handoff do pq-6: Worker musi czekać na zakończenie migracji Api, nie tylko na healthy Postgres.** `pg_isready` potwierdza życie Postgresa, nie istnienie schematu — naiwne `worker depends_on: postgres (healthy)` da przy pierwszym boocie „relation prompts does not exist". Mechanizm (healthcheck Api / kontener migracyjny) wybiera pq-6.
- **Handoff do pq-3: w pętli workera użyć `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)`.** Placeholder już to stosuje. Gdy pq-3 wstawi realne I/O (DB, HTTP do Ollamy), stray `TaskCanceledException` (np. timeout `HttpClient`) nie może być mylony z shutdownem — filtr `when` kieruje go do `catch (Exception)` (log + kontynuacja), zamiast trwale ubić workera.
- **Indeks na `Status`/`CreatedAt` i token współbieżności — świadomie odłożone do pq-3.** Przy skali demo seq scan jest bez znaczenia, a token współbieżności można dodać przez systemowy `xmin` Postgresa bez migracji. Zakres pq-3, nie luka fundamentu.
- **Connection string `ConnectionStrings__PromptQueue` tylko z env var, fail-fast przy braku** (bez cichego fallbacku) — zgodnie z CLAUDE.md „Stack decisions" i zasadą SSOT/no-fallback. Jeden mechanizm w Api, Workerze i factory design-time.

## Plan implementacji

1. Utwórz solution i projekty pod `backend/src/` (`classlib` Domain/Infrastructure, `web` Api, `worker` Worker) i referencje (Infra→Domain, Api→Infra, Worker→Infra) — `backend/PromptQueue.sln`.
2. Dodaj do Infrastructure pakiety `Npgsql.EntityFrameworkCore.PostgreSQL` (10.x) i `Microsoft.EntityFrameworkCore.Design` — `backend/src/PromptQueue.Infrastructure/PromptQueue.Infrastructure.csproj`.
3. Zaimplementuj `PromptStatus`, `Prompt` (`Guid.NewGuid()` + przejścia + bramki) i `IPromptRepository` (`Guid`) — `backend/src/PromptQueue.Domain/Prompts/`.
4. Zaimplementuj `PromptQueueDbContext` i `PromptConfiguration` (`Id` → `ValueGeneratedNever`) — `backend/src/PromptQueue.Infrastructure/`.
5. Zaimplementuj `PromptRepository` (`GetByIdAsync(Guid)`, `GetAllAsync` sort `CreatedAt`+`Id`) — `backend/src/PromptQueue.Infrastructure/Persistence/Repositories/PromptRepository.cs`.
6. Dodaj `AddInfrastructure`, `DesignTimeDbContextFactory`, `MigrationExtensions` (log start/koniec, try/catch + rethrow) — `backend/src/PromptQueue.Infrastructure/`.
7. Wygeneruj migrację (`uuid` PK): `dotnet ef migrations add InitialCreate -p backend/src/PromptQueue.Infrastructure -s backend/src/PromptQueue.Infrastructure -o Persistence/Migrations` — `backend/src/PromptQueue.Infrastructure/Persistence/Migrations/`.
8. Napisz host Api: `AddInfrastructure`, `AddExceptionHandler<GlobalExceptionHandler>()` + `AddProblemDetails()` + `app.UseExceptionHandler()`, `await app.Services.ApplyMigrationsAsync()`, `GET /health`, klasa `GlobalExceptionHandler`, poziomy logów w `appsettings` — `backend/src/PromptQueue.Api/`.
9. Napisz host Worker z placeholderowym `PromptProcessingWorker : BackgroundService` (log started/stopping, try/catch w pętli z filtrem `when`) — `backend/src/PromptQueue.Worker/`.
10. Dodaj `docker-compose.yml` z usługą `postgres:alpine` (env `POSTGRES_*`, wolumen, healthcheck `pg_isready`) — `docker-compose.yml`.
11. Dodaj projekt testów domeny (xUnit) i testy przejść stanu + generacji Id — `backend/tests/PromptQueue.Domain.Tests/`.
12. Weryfikacja DoD (PowerShell): `docker compose up -d postgres`; `$env:ConnectionStrings__PromptQueue = '...'`; `dotnet ef database update -p backend/src/PromptQueue.Infrastructure -s backend/src/PromptQueue.Infrastructure`; `dotnet run --project backend/src/PromptQueue.Api` → w konsoli logi „Applying/Applied migrations", `/health` 200 i tabela `prompts` (kolumna `id uuid`) w bazie.

## Strategia testowania

- **Domain (jednostkowe, xUnit) — priorytet.** Cała logika pq-1 jest tu; pokryć: konstruktor ustawia `Pending`; `StartProcessing` z `Pending`→`Processing`; `Complete` tylko z `Processing` ustawia `Result`+`Completed`; `Fail` z `Pending`/`Processing` ustawia `ErrorMessage`+`Failed`; przejścia ze stanów terminalnych, `Complete` spoza `Processing` oraz `StartProcessing` spoza `Pending` rzucają `InvalidOperationException`; pusty `Content` rzuca `ArgumentException`. Testy generacji `Guid` i znaczników czasu świadomie pominięte (zachowanie frameworka / trywialne, nie logika domeny). Nazewnictwo: konwencja `Should_<Wynik>_When<Warunek>`.
- **Infrastructure / migracja (weryfikacja manualna).** W pq-1 wystarczy checklista DoD (krok 12): migracja aplikuje schemat z kolumną `id uuid`, Api łączy się z bazą i loguje przebieg migracji. Automatyczny test integracyjny na Testcontainers-PostgreSQL wprowadzić dopiero z endpointami w pq-2 — teraz to nadmiarowa inwestycja. `IPromptRepository` daje pq-2/pq-3 szew do mockowania w testach jednostkowych warstwy aplikacji.
- **Szew błędów/logowania — bez dedykowanych testów w pq-1.** `GlobalExceptionHandler` jest w pq-1 nieuruchamialny (brak endpointu, który rzuca; w Development i tak przechwytuje `DeveloperExceptionPage`) — testy zachowania handlera dokłada pq-2. Odporność pętli Workera to placeholder — testy dokłada pq-3 razem z realną pętlą. Dodawanie ich teraz to over-engineering przy braku logiki do przetestowania.

## Pytania do użytkownika

Brak otwartych pytań. Wszystkie decyzje (`Guid` v7, repozytorium, `net10.0`, szew błędów/logowania) rozstrzygnięte decyzjami użytkownika 2026-07-15 (patrz § Kluczowe decyzje). Elementy poza tym taskiem odnotowane jako handoffy: aktualizacja przykładu POST w pq-2 (Guid), synchronizacja startu Worker→migracja w pq-6, filtr `when` w pętli workera w pq-3.

## Krytyka (solution-critic)

Werdykty: v1 **AKCEPTUJ Z UWAGAMI** (bez blokerów), v3 **AKCEPTUJ Z UWAGAMI** (bez blokerów) — po ocenie zmian Guid + szew błędów/logowania. Wszystkie uwagi istotne i wartościowe obserwacje z v3 naniesione: deterministyczna kolejność `GetAllAsync` (sort `CreatedAt` + tie-break `Id`), samodzielność dokumentu (pełne bloki kodu zamiast „patrz v2"), doprecyzowanie „Id znany przed zapisem" (nie „bez round-tripu"), filtr `when` w pętli workera (handoff pq-3), nota o niezweryfikowanym `GlobalExceptionHandler`, przycięte uzasadnienie Guid. Wariant identyfikatora UUID v7 rozważony (usuwa fragmentację indeksu), ale odrzucony decyzją użytkownika na rzecz `Guid.NewGuid()` (v4) — przy skali demo fragmentacja bez znaczenia. Poprawność faktury .NET 10 (Npgsql `Guid`→`uuid`, `ValueGeneratedNever`, `UseExceptionHandler()` + `AddProblemDetails`, domyślny provider konsolowy) zweryfikowana przez krytyka względem dokumentacji MS. Pełna treść krytyk nie jest zachowana w repo (wersje nie były commitowane przed kondensacją).

## Historia wersji

- v1 (2026-07-15): projekt system-architect + krytyka (AKCEPTUJ Z UWAGAMI, bez blokerów).
- v2 (2026-07-15): decyzje użytkownika — `IPromptRepository` zamiast gołego `DbContext`, `net10.0` (SDK 10.0.109); naniesione uwagi krytyka v1 (fail-loud zamiast „idempotencji", handoff pq-6, deferral indeksu/`xmin`, konsolidacja connection stringa); audit skondensowany.
- v3 (2026-07-15): feedback użytkownika — identyfikator `int` → `Guid` (`Guid.NewGuid()`, generowany w konstruktorze; wariant v7 rozważony, odrzucony przez użytkownika) oraz minimalny szew obsługi błędów i logowania (wbudowane `Microsoft.Extensions.Logging`, `GlobalExceptionHandler`, fail-fast migracji, odporna pętla Workera); naniesione uwagi krytyki v3 (patrz § Krytyka).
