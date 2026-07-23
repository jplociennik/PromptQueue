# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is being built

PromptQueue lets a user submit multiple LLM prompts at once and track each one through its lifecycle: `pending → processing → completed | failed`. Every prompt is persisted; a separate worker executes them against a local LLM (Ollama). The authoritative requirements source is [doc/prepare-projects/DoD.md](doc/prepare-projects/DoD.md) (Polish) — treat it as read-only spec.

The system is implemented and runs end-to-end via `docker compose up`. Per-stage technical designs live in [doc/projects/](doc/projects/) and implementation reports in [doc/implementation-reports/](doc/implementation-reports/) (pq-1 foundation/DB, pq-2 API, pq-3 worker/Ollama, pq-4/pq-5 frontend, pq-6 orchestration).

## Run / build / test

**Whole system** (from repo root): `docker compose up` → app at **http://localhost:8080**. First run builds images and pulls the `llama3.2` model (~2 GB). `docker compose down` stops (keeps volumes); `down -v` resets. Ports and env vars are documented in [README.md](README.md); copy `.env.example` → `.env` to override defaults.

**Backend** (`.sln` at [backend/PromptQueue.sln](backend/PromptQueue.sln), .NET 10):
- Build via the user's global PowerShell shortcuts — `. $PROFILE.CurrentUserAllHosts; all-build` (see global CLAUDE.md), or `dotnet build` from `backend/`.
- Tests: `dotnet test` from `backend/`. **Integration tests require Docker** (Testcontainers spins up Postgres).
- Run a single service locally against compose infra: `docker compose up -d postgres ollama`, set `ConnectionStrings__PromptQueue`, then `dotnet run --project backend/src/PromptQueue.Api` (and `.Worker`). Details in README "Uruchamianie komponentów osobno".

**Frontend** (`frontend/`, Vite + React + TS): `npm install`, `npm run dev` (Vite dev server, proxies `/api` → `http://localhost:5269`), `npm run build`, `npm run test` (Vitest).

## Architecture

Backend is layered (clean-architecture style), four `src` projects under [backend/src/](backend/src/):

- **PromptQueue.Domain** — no dependencies. Rich `Prompt` entity ([Prompt.cs](backend/src/PromptQueue.Domain/Prompts/Prompt.cs)) is the SSOT for state-transition rules: `StartProcessing`/`Complete`/`Fail`/`Requeue` are guarded methods that throw on illegal transitions; all setters are private. `PromptStatus` enum + `IPromptRepository` interface also live here.
- **PromptQueue.Infrastructure** — EF Core 10 + Npgsql. `PromptQueueDbContext`, `IEntityTypeConfiguration` mappings, `PromptRepository`, migrations, and `AddInfrastructure(config)` DI (reads connection string `PromptQueue`, fail-fast if missing).
- **PromptQueue.Api** — ASP.NET Core Minimal API. Endpoints in [PromptEndpoints.cs](backend/src/PromptQueue.Api/Prompts/PromptEndpoints.cs): `POST /api/v1/prompts` (batch add + validation), `GET /api/v1/prompts`, `GET /api/v1/prompts/{id}`, plus `GET /health`. **The API applies EF migrations on startup** (`ApplyMigrationsAsync`) — it owns the schema; the worker never migrates. Enums serialize as camelCase strings (e.g. `"pending"`) to match the frontend. DTOs/mapping/validator sit next to the endpoints; domain entity never leaves the API boundary.
- **PromptQueue.Worker** — .NET Generic Host `BackgroundService` ([PromptProcessingWorker.cs](backend/src/PromptQueue.Worker/PromptProcessingWorker.cs)) that loops on an interval; per-cycle logic is delegated to the testable scoped `PromptProcessor` ([PromptProcessor.cs](backend/src/PromptQueue.Worker/PromptProcessor.cs)). On startup it requeues interrupted (`Processing`) prompts and waits for model readiness; each cycle drains `Pending` in batches, calls the model with one retry, and persists result or error. LLM access is via `Microsoft.Extensions.AI` `IChatClient` backed by `OllamaSharp` (`AddPromptProcessing` DI). Tunables (`OllamaBaseUrl`, `OllamaModel`, poll/batch/timeout/retry) are in `WorkerOptions` / `appsettings.json` / env (`Worker__*`).

Tests live under [backend/tests/](backend/tests/): Domain/Api/Worker unit tests, Api/Worker integration tests, and a shared `PromptQueue.TestSupport` (builders, `FakeChatClient`, API client, assertions).

**Frontend** ([frontend/src/](frontend/src/)) — Vite + React 18 SPA in TypeScript, served in prod by nginx (which also proxies `/api` → api). Structure: `api/` (axios client + typed contracts + `prompts.ts`), `components/UI/` (reusable presentational: Button, TextArea, Alert, StatusBadge), `features/prompts/` (PromptForm, PromptField, PromptList, PromptRow), `hooks/` (`useCreatePrompts`, `usePromptFields`, `usePromptPolling` — polling drives status refresh, no websockets). Each hook/component is its own folder with a colocated `.test.ts(x)` and `.module.css`.

## Stack decisions (rationale)

- **Frontend: Vite + React SPA, TypeScript — not Next.js.** The C# backend owns data/API, there's no SSR/SEO need, and refresh is client-side polling — a plain client-rendered SPA is the simplest fit and keeps orchestration to backend + worker + static frontend (no extra Node server). Revisit only if SSR/SEO becomes a requirement.
- **Database: PostgreSQL** (`postgres:18-alpine`). Chosen over SQLite because API and worker write concurrently (Postgres handles it without SQLite's single-writer locking), over SQL Server for a lighter image. **Host port is `5433`** (not 5432) to avoid clashing with a native Postgres; inside the compose network it's still 5432.
- **App LLM: local model via Ollama, not a paid API.** Worker calls Ollama over HTTP through `Microsoft.Extensions.AI` `IChatClient` (Ollama default, swappable via config). No API keys, runs with one `docker compose up`. Model + base URL come from env vars; `ollama-pull` is a one-shot compose service that fetches the model into a shared volume.
- **Orchestration: docker-compose** starts everything (postgres + ollama + ollama-pull + api + worker + frontend) with correct `depends_on` health/completion gating.

## Conventions

- **Coding-style rules live in referential skills, not here:** [.claude/skills/code-backend/SKILL.md](.claude/skills/code-backend/SKILL.md) (C#/.NET: rich domain with guards, primary-constructor DI, EF config via `IEntityTypeConfiguration`, connection string from env fail-fast, `Should_…` test naming — test logic not framework) and [.claude/skills/code-frontend/SKILL.md](.claude/skills/code-frontend/SKILL.md) (React SPA style). Load the relevant skill before writing code in that layer. The `implementator` / `code-reviewer` / `system-architect` agents enforce these.
- Project documentation may be written in Polish; keep DoD.md as-is and treat it as the requirements source.
