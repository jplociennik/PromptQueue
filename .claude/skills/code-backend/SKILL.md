---
name: code-backend
description: "Wytyczne stylu kodu backendu (C# / .NET, ASP.NET Core, EF Core): bogata domena z bramkami, primary-constructor DI, konfiguracje EF przez IEntityTypeConfiguration, connection string z env (fail-fast), oraz konwencja testów jednostkowych (nazwy Should_…, testuj logikę nie framework). Załaduj gdy tworzysz lub modyfikujesz kod backendu: encje, serwisy, endpointy, DbContext, repozytoria, migracje, testy."
user-invocable: false
---

# Styl kodu backendu (C# / .NET + ASP.NET Core + EF Core)

> Uzupełnia reguły z agentów `system-architect` / `implementator` (architektura, podział warstw). Tam są zasady architektoniczne — **tu konkretne nawyki zapisu kodu backendu**. Stack PromptQueue: C# / .NET 10, ASP.NET Core, EF Core + Npgsql/PostgreSQL. Odpowiednik frontendowego skilla `code-frontend`.

## Zasada nadrzędna

**Prostota i minimalizm (KISS / YAGNI)** — pisz najmniej kodu potrzebnego do rozwiązania; nie dodawaj abstrakcji „na zapas". Każda reguła niżej służy tej zasadzie.

## Nazewnictwo i komentarze

1. **Identyfikatory po angielsku, dokumentacja `<summary>` po polsku** (zwięźle, ≤ 2 linie). Bez komentarzy do oczywistości.
2. Namespace zgodny ze strukturą folderów (folder = namespace).

## Domena

3. **Bogata encja, nie anemiczna** — przejścia stanu jako metody (`StartProcessing()`, `Complete(result)`, `Fail(error)`), nie publiczne settery. Encja jest SSOT swojego stanu.
4. **Bramki fail-loud** — nielegalna operacja rzuca (`InvalidOperationException` / `ArgumentException`), nie przechodzi po cichu. Walidacja argumentów w konstruktorze/metodzie.

## DI i konfiguracja

5. **Primary-constructor DI** — wstrzykuj przez konstruktor główny, bez prywatnych pól-przypisań:
   ```csharp
   public class PromptRepository(PromptQueueDbContext dbContext) : IPromptRepository
   ```
6. **Rejestracja w `AddInfrastructure`/`Add<Warstwa>`** — jedno miejsce składania warstwy (extension method na `IServiceCollection`).
7. **Connection string / sekrety z env var, fail-fast** — brak wartości → rzuć od razu (`?? throw`), bez cichego fallbacku. Jedno źródło (SSOT).

## EF Core

8. **Konfiguracja encji przez `IEntityTypeConfiguration<T>`** + `ApplyConfigurationsFromAssembly` w `OnModelCreating` — nie konfiguruj fluent-API inline w `DbContext`.
9. **`DbContext` = Unit-of-Work + repozytorium.** Dodatkowe repozytorium tylko gdy chcesz jawny szew (np. testowy) — nie mnóż abstrakcji bez powodu.
10. **Migracje są auto-generowane** (`dotnet ef migrations add`) — nie edytuj ręcznie `Up`/`Down`/snapshotu; zmiana modelu = nowa migracja. Enum mapuj jako string (`HasConversion<string>()`) dla czytelności w bazie.

## Testy

11. **Nazwa testu: `Should_<Wynik>_When<Warunek>`** (xUnit). Przykłady:
    ```
    Should_TransitionToProcessing_WhenStartingFromPending
    Should_ThrowInvalidOperation_WhenCompletingFromPending
    Should_ThrowArgumentException_WhenContentIsBlank
    ```
12. **Testuj logikę, nie framework.** Sprawdzaj reguły biznesowe (maszyna stanów, bramki, walidacje). **Nie pisz testów** na zachowanie frameworka (`Guid.NewGuid()` daje unikat, ORM mapuje pole) ani trywialne szczegóły implementacyjne (np. `CreatedAt == UpdatedAt` w konstruktorze) — to szum, nie wartość.
13. **Struktura AAA**, testy deterministyczne — unikaj asercji zależnych od zegara (`UpdatedAt > CreatedAt` bywa flaky przez rozdzielczość czasu); jeśli musisz, użyj `>=` albo wstrzykniętego zegara.
14. **Testy integracyjne (Testcontainers) wtedy, gdy jest realne I/O do sprawdzenia** (endpoint + baza), nie na zapas dla samego szkieletu. Domenę testuj jednostkowo.

## Czego ta reguła NIE narzuca

- Konkretnych bibliotek (walidacja, mapowanie, mediator) — to decyzje projektowe (`CLAUDE.md` / projekt z `/design`).
