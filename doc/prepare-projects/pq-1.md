# pq-1 — Fundament: struktura rozwiązania + baza

## Opis

**Tytuł:** Fundament systemu i trwały zapis promptów w bazie

System trwale zapisuje każdy zgłoszony prompt wraz z jego statusem, dzięki czemu żadne zgłoszenie nie ginie i można śledzić jego cykl życia. To zadanie kładzie fundament pod pozostałe: strukturę rozwiązania, model danych i bazę.

## Projekt wstępny

Cel: położyć fundament, na którym stają wszystkie kolejne taski — struktura solution, model danych i persystencja. Bez logiki API/workera/frontu (te w [pq-2](pq-2.md), [pq-3](pq-3.md), [pq-4](pq-4.md), [pq-5](pq-5.md)).

Stack (za `CLAUDE.md` → „Stack decisions"): C# / ASP.NET Core + EF Core, **PostgreSQL** (Npgsql). Bez pełnego DDD — domena jest cienka.

Zakres:
- **Struktura solution** (propozycja): `PromptQueue.Api` (ASP.NET Core), `PromptQueue.Worker` (proces w tle), `PromptQueue.Domain` (encja `Prompt` + reguły przejść stanu), `PromptQueue.Infrastructure` (EF Core `DbContext`, repozytorium, migracje). Warstwy lekkie, nie ciężkie DDD.
- **Encja `Prompt`**: identyfikator, treść, status, wynik (nullable), komunikat błędu (nullable), znaczniki czasu (utworzenie/aktualizacja). Status jako enum: `Pending`, `Processing`, `Completed`, `Failed` — nazwy stanów z [DoD.md L5](DoD.md#L5).
- **Przejścia stanu jako metody encji** (np. `StartProcessing()`, `Complete(result)`, `Fail(error)`), a nie anemiczne settery — tyle „porządku", ile bez ceremonii.
- **EF Core + Npgsql**: `DbContext`, konfiguracja encji, pierwsza migracja tworząca schemat. Connection string z zmiennej środowiskowej.
- **docker-compose (dev)**: usługa `postgres` (`postgres:alpine`), żeby dało się lokalnie postawić bazę. Pełny compose (API/worker/front/Ollama) domyka [pq-6](pq-6.md).

Definition of Done (tasku): rozwiązanie się kompiluje; migracja tworzy schemat; `docker compose up` stawia Postgres; aplikacja łączy się z bazą.

## Do wyjaśnienia

- [?] Typ identyfikatora promptu: `int` (autoincrement) czy `Guid`? — wpływa na klucze i URL-e w GET ([pq-2](pq-2.md)).
- [?] Czy prompt ma osobne pole nazwa/tytuł, czy tylko treść? — DoD mówi wyłącznie o „promptach".
