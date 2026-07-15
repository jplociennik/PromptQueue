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

## Conventions
- Backend: primary-constructor DI (C# 12), async+CancellationToken on I/O, English names / Polish `<summary>`.
- React style rules live in skill `code-frontend`.
