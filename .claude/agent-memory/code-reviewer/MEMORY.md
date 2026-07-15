# Pamięć code-reviewer — PromptQueue

Greenfield: C#/.NET 10 (`net10.0`) backend + Worker + React SPA (TS) + PostgreSQL + Ollama, orkiestracja docker-compose.
SSOT wierności implementacji: `doc/projects/pq-N.md` (zaakceptowany projekt). Materiały wejściowe: `doc/prepare-projects/`.
Index jednolinijkowy — szczegóły w plikach tematycznych; przed przeglądem danej warstwy przeczytaj właściwy plik.

## Backend (C# / .NET) — [backend.md](backend.md)
- Warstwy (egzekwuj kierunek ref.): `Domain` ◄ `Infrastructure` ◄ `Api`/`Worker`; Domain ZERO referencji (bez EF).
- Konwencje: id EN, `<summary>` PL na publicznych; Primary Constructor DI bez prywatnych pól; `Nullable`+`ImplicitUsings` enable.
- Domena `Prompt`: enkapsulacja, `Id`=Guid v4 w ktorze, przejścia z bramkami fail-loud; `PromptStatus{Pending,Processing,Completed,Failed}`; dodanie metody domenowej ⇒ 0 migracji.
- EF/Npgsql: snapshot/Designer w 100% z automatu (Npgsql emituje `UseIdentityByDefaultColumns` — NORMALNE, nie ręczna edycja); zmiana modelu ⇒ nowa migracja+snapshot; conn-string env-only fail-fast; migruje TYLKO Api na starcie.
- Api: Minimal API `PromptEndpoints`, DTO `record` `PromptContracts.cs`, walidacja=czysta fn (klucze `prompts`/`prompts[i]`), JSON enum camelCase, 200/400/404/500, `partial Program`. dev-CORS pq-4 = `AddCors()`+dev-only `UseCors` PO `UseExceptionHandler`.
- Worker (pq-3): POWŁOKA(`BackgroundService`)/LOGIKA(`PromptProcessor` scoped) rozdzielone, scope-per-cykl; podwójny-filtr OCE; retry=1 in-mem; `IChatClient`/OllamaSharp; `WorkerOptions` fail-fast; Worker NIE migruje.

## Testy (backend, xUnit) — [testing.md](testing.md)
- `*.UnitTests`/`*.IntegrationTests` (Testcontainers `postgres:18-alpine`), shared `PromptQueue.TestSupport`; AwesomeAssertions 9.4.0; AssertionScope (await/materializuj PRZED scope); buildery/`PromptApiClient`/`ProblemDetailsAssertions`; izolacja przez asercje na WŁASNYCH id.
- POWTARZALNE: przy maszynie stanów sprawdzaj test negatywny KAŻDEJ gałęzi `throw`.

## Frontend (React SPA — od pq-4) — [frontend.md](frontend.md)
- Vite+React18+TS strict, Vitest+RTL (`globals:false`). Struktura = skill code-frontend REGUŁA 9: folder-per-component, `components/UI/` (UI wielką), hooki płasko w `hooks/`, bez barreli, CSS współlokowany.
- HTTP=axios (`http`+`ApiError`+czysta `toApiError`+interceptor) — ODSTĘPSTWO od doc(fetch/apiFetch). `api/types.ts`=wierne lustro pq-2.
- WZORCE (nie flaguj): reset po sukcesie (`submit:Promise<boolean>`), blokada double-submit `disabled` gdy submitting, reducer=switch, bez przedwczesnej memoizacji, a11y aria-label/aria-invalid/role=alert.
- POWTARZALNE: (1) usunięcie ostatniego pola→`[].some()===false`→pusty POST→400; (2) `validationErrors` łapane ale NIE renderowane (Alert=tylko generyczny EN title); (3) `globals:false`→RTL auto-cleanup off.

## docker-compose — [backend.md](backend.md#docker-compose)
- Postgres `postgres:18-alpine` (PINUJ major!), wolumen na parent `/var/lib/postgresql`; Ollama `ollama/ollama:0.32.0`, healthcheck `ollama list`.

## Historia przeglądów — [review-history.md](review-history.md)
- pq-1 Fundament: AKCEPTUJ Z UWAGAMI · pq-2 API: AKCEPTUJ · pq-2 refaktor testów: AKCEPTUJ · pq-3 Worker+Ollama: AKCEPTUJ Z UWAGAMI · pq-4 Frontend: AKCEPTUJ Z UWAGAMI.
