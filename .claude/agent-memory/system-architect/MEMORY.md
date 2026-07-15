# System Architect — PromptQueue

Greenfield C#/.NET backend + Vite/React/TS frontend. Stack decisions live in root `CLAUDE.md` (don't duplicate here). Spec (PL): `doc/prepare-projects/DoD.md`. Per-task preliminary designs: `doc/prepare-projects/pq-1.md`..`pq-6.md`. Finalized designs: `doc/projects/pq-N.md`.

## Task graph (dependencies)
- pq-1 — Fundament: solution structure + Domain/Infrastructure + `Prompt` entity + EF Core/Npgsql + first migration + docker-compose(postgres only).
- pq-2 — API (`POST/GET /api/v1/prompts`, GET by id). Depends pq-1.
- pq-3 — Worker (`BackgroundService`) + Ollama via `Microsoft.Extensions.AI` `IChatClient`. Depends pq-1.
- pq-4 — Frontend setup + add-prompts form. Depends pq-2.
- pq-5 — Frontend list + status polling. Depends pq-2, pq-4.
- pq-6 — Full docker-compose + README. Postgres lands in pq-1, Ollama in pq-3, full compose here.

## Cross-task signals (verified from specs)
- Prompt id = **Guid** (user decision 2026-07-15, was int in v2). Generated in entity ctor `Id = Guid.NewGuid()`, `ValueGeneratedNever()`, Npgsql maps -> `uuid`. Id known without DB round-trip (POST can return id before SaveChanges).
- HANDOFF pq-2: `pq-2.md` L16 example `{ "ids": [1, 2] }` is now stale -> must become Guid strings; GET-by-id route should use `{id:guid}`. Do NOT edit pq-2.md from pq-1 work; recorded as handoff inside `doc/projects/pq-1.md`.
- Prompt has **content only**, no title — DoD, pq-2/4/5 reference only prompt text.
- Status enum names from DoD L5: Pending / Processing / Completed / Failed (PL: oczekujace/przetwarzane/zakonczone/nieudane).
- Connection string from env var `ConnectionStrings__PromptQueue`, fail-fast, no fallback (CLAUDE.md). DB = PostgreSQL (Npgsql), `postgres:alpine`.

## pq-1 confirmed design (user decisions 2026-07-15)
- 4 projects under `src/`; Domain pure (entity+enum+`IPromptRepository` port), Infrastructure->Domain (DbContext, config, `PromptRepository`, migrations, DI, design-time factory, `MigrationExtensions`), Api/Worker->Infrastructure.
- Repository: `IPromptRepository` (Domain) + `PromptRepository` (Infra) — explicit test seam; procs never touch DbContext directly.
- Enum stored as string; timestamps `DateTime.UtcNow` in entity -> Postgres `timestamptz`. `GetAllAsync` orders by `CreatedAt` (Guid id carries no insertion order).
- Api owns migrate-on-startup (`ApplyMigrationsAsync`); Worker never migrates. Handoff pq-6: worker must wait for Api migration, not just healthy Postgres.
- .NET 10 LTS, `net10.0` — confirmed SDK 10.0.109.

## pq-1 error handling + logging (user decision 2026-07-15, "simplest possible")
- Built-in `Microsoft.Extensions.Logging` (console), NO Serilog/external pkgs. Serilog = optional later swap (single registration point), not foundation. Levels in appsettings: Default Information, `Microsoft.EntityFrameworkCore` Warning (no SQL-command spam).
- Api: `GlobalExceptionHandler : IExceptionHandler` (net8+) via `AddExceptionHandler` + `AddProblemDetails` + `UseExceptionHandler` — logs + ProblemDetails 500 (uses `IProblemDetailsService`). Host infra seam, not HTTP logic; pq-2 endpoints reuse it.
- `MigrationExtensions.ApplyMigrationsAsync`: resolves `ILogger`, logs start/done, try/catch -> LogError + rethrow (fail-fast, host won't start on broken DB). Only real error path in pq-1.
- Worker `PromptProcessingWorker` placeholder: logs started/stopping + try/catch around loop body (log + continue) so exceptions don't kill process — foundation for pq-3 "worker doesn't crash".

## pq-2 implemented (state as of 2026-07-15) + test conventions
- pq-2 done: build 0/0, tests 33/33. 3 Minimal API endpoints in `Api/Prompts/`, no service class; validator = pure fn. `public partial class Program;` for WebApplicationFactory.
- Test conventions (fundament pq-3+): `*.UnitTests` (no I/O) / `*.IntegrationTests` (Testcontainers `postgres:18-alpine`). Shared `PromptQueue.TestSupport` (refs Api) = `PromptApiClient`, `CreatePromptsRequestBuilder`, `ProblemDetailsAssertions`. **AwesomeAssertions 9.4.0** (namespace `AwesomeAssertions`, asserts-at-bottom + `AssertionScope`), NOT xUnit Assert, NOT FluentAssertions v8+. **No mocking library** — hand-written test doubles (e.g. throwing repo stub via `ConfigureTestServices`).
- SDK confirmed 10.0.110. `Microsoft.EntityFrameworkCore.Relational` pinned 10.0.10 in Infrastructure (MSB3277 fix; Design `PrivateAssets=all` doesn't propagate version).
- CR lesson (pq-1 O2): pin docker image tags, never floating `latest`.

## pq-3 LLM facts (verified web 2026-07)
- Abstraction stays `IChatClient` (`Microsoft.Extensions.AI`) per CLAUDE.md; method `GetResponseAsync(...)` -> `ChatResponse.Text`.
- Ollama provider = **OllamaSharp** pkg (`OllamaApiClient` implements `IChatClient`). `Microsoft.Extensions.AI.Ollama` is **DEPRECATED** — do not use.
- `BackgroundService` = singleton -> scoped DbContext/repo ONLY via `IServiceScopeFactory.CreateScope()` per cycle. Split shell (loop/timing/scope) from testable scoped processor.
- Loop filter `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` distinguishes shutdown from stray `TaskCanceledException` (HttpClient timeout).

## pq-4 frontend foundation (designed 2026-07-15, verified facts)
- Frontend monorepo dir = `frontend/` (sibling of `backend/`), empty at design time. Paths `frontend/src/...`.
- API Development http port = **5269** (`backend/.../PromptQueue.Api/Properties/launchSettings.json`, http profile) — Vite dev proxy target.
- Recommended: Vite `server.proxy['/api'] -> http://localhost:5269` so dev needs NO backend CORS; base URL via `VITE_API_BASE_URL` (default '', relative). Prod/compose (pq-6) reverse-proxies `/api` (nginx) -> CORS stays unneeded. This resolves the CORS deferral from pq-2.
- Consumed contract (pq-2, camelCase JSON): POST `/api/v1/prompts` `{prompts:string[]}` -> 200 `{ids:string[],status:'pending'}`; 400 `application/problem+json` with `errors` (keys `prompts`, `prompts[i]`). PromptStatus enum -> camelCase strings `'pending'|'processing'|'completed'|'failed'`. Limits (SSOT backend, don't dup): 50 prompts, 8000 chars/each after trim.
- pq-5 handoff: pre-declare `PromptResponse` in `frontend/src/api/types.ts` and single HTTP layer `api/` so pq-5 adds only `getPrompts` + polling hook, no rework.

## pq-5 frontend list + polling (designed 2026-07-15)
- pq-4 shipped exactly as designed. Actual paths: `api/prompts.ts` (`createPrompts`, add `getPrompts`), `api/client/client.ts` (axios `http`+`ApiError`+`toApiError`), `hooks/X/X.ts`+`.test.ts`, `features/prompts/{PromptForm,PromptField}/`, `components/UI/{Button,TextArea,Alert}/`. `App.tsx` = composition root.
- `PromptResponse` already in `api/types.ts` (id,content,status,result:string|null,errorMessage:string|null,createdAt,updatedAt). `PromptStatus` union of 4.
- Vitest `globals:false` → tests import `{describe,it,expect,vi,beforeEach,afterEach}` from 'vitest'. RTL auto-cleanup NOT registered → render/renderHook tests MUST `afterEach(cleanup)` (+ `vi.useRealTimers()` for timer tests). Confirmed pattern in `useCreatePrompts.test.ts`.
- `main.tsx` wraps `<StrictMode>` → effects double-invoke in dev; polling cleanup must be StrictMode-safe (validates teardown).
- Polling hook pattern chosen: recursive `setTimeout` (no overlap) + `AbortController` + `cancelled` flag; refetch = bump a `trigger` useState in effect deps (restart loop = teardown+immediate tick, natural resume). useReducer for {prompts,status,error} (mirrors `useCreatePrompts`), stale-while-revalidate on error (keep prompts).
- StatusBadge: `Record<PromptStatus,string>` labels + `styles[status]` class (same idiom as Alert/Button `styles[variant]`); Record forces exhaustiveness at compile time.
- Component-class idiom in repo: `[styles.base, styles[variant], className].filter(Boolean).join(' ')`.

## pq-6 orchestration (designed 2026-07-15)
- HARD user decision: postgres host mapping **5433:5432** (native PG occupies host 5432; conflict hit during pq-5 manual verify). Inside compose api/worker use `postgres:5432`. All host-side conn strings (dotnet ef, psql) → 5433.
- Api `GET /health` = 200 static, mapped AFTER `await ApplyMigrationsAsync()` (before `app.Run()`) ⇒ **health=200 ⟹ schema ready**. Gate `worker depends_on api condition:service_healthy` solves the pq-1/pq-3 handoff with ZERO worker code change. Api healthcheck needs curl → install in runtime stage (aspnet Debian image lacks it).
- Frontend serving = **nginx** (multi-stage node build → nginx serve) + reverse-proxy `location /api/ → http://api:8080`. Keeps single origin, `VITE_API_BASE_URL` empty (relative /api), NO prod-CORS. Vite preview rejected (dev server).
- Model auto-pull = one-shot init service `ollama-pull` (image ollama/ollama, `OLLAMA_HOST=http://ollama:11434`, `ollama pull ${OLLAMA_MODEL}`, restart:no) depends_on ollama healthy; worker depends_on it `service_completed_successfully`. ~2GB first-up cost, cached in ollama-data volume. Marked proposed (lengthens first up).
- Docker build contexts: api/worker `context: ./backend` (appsettings.json linked via `..\..\`), dockerfile `src/PromptQueue.X/Dockerfile`; frontend `context: ./frontend`. Api container listens 8080; compose conn string `Host=postgres;Port=5432;...`.
- `.env.example` at root: OLLAMA_MODEL, POSTGRES_USER/PASSWORD/DB (compose interpolation `${VAR:-default}`). Conn string for design-time EF stays host-side env-only, now port 5433.

## Conventions
- Backend: primary-constructor DI (C# 12), async+CancellationToken on I/O, English names / Polish `<summary>`.
- React style rules live in skill `code-frontend`.
