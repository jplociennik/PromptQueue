# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Greenfield — no code exists yet. The repository contains only the project specification. Once scaffolding lands, update this file with the actual build/run/test commands and architecture.

## What is being built

The authoritative spec is [doc/prepare-projects/DoD.md](doc/prepare-projects/DoD.md) (written in Polish). Summary:

PromptQueue is a system for submitting multiple LLM prompts and tracking their processing status:

- **Backend (C#)** — exposes an API to add prompts and fetch their current states; every prompt is persisted in a database.
- **Worker (separate process)** — picks up queued prompts and executes them using an LLM client library (local model or external service such as the Claude/OpenAI API).
- **Prompt lifecycle** — every task must move through the states: pending (oczekujące) → processing (przetwarzane) → completed (zakończone) or failed (nieudane).
- **Frontend (React SPA)** — Vite + React in **TypeScript**; lets the user submit multiple prompts and shows a list of all prompts with current statuses and results; refresh via simple polling (no websockets required).
- **Orchestration** — the whole system should start with a single command (e.g. docker compose), accompanied by short run/setup documentation.

## Stack decisions

- **Frontend: Vite + React (SPA), TypeScript — not Next.js.** The whole frontend is TypeScript (`.tsx`/`.ts`). The C# backend owns the data and API, there is no SEO/SSR need, and the status refresh is client-side polling — so a plain client-rendered SPA is the simplest fit and keeps orchestration to backend + worker + static frontend (no extra Node server). Revisit only if SSR/SEO becomes a requirement.
- **React conventions** the agents enforce: functional components + hooks, composition/SRP with reusable UI components, logic in custom hooks, `useState`/`useReducer` for state, memoization only where it measurably pays off. Full rules live in [.claude/agents/system-architect.md](.claude/agents/system-architect.md), [.claude/agents/implementator.md](.claude/agents/implementator.md) and the referential skill [.claude/skills/code-frontend/SKILL.md](.claude/skills/code-frontend/SKILL.md).
- **Database: PostgreSQL** (EF Core + Npgsql), a docker-compose service (`postgres:alpine`). Chosen over SQLite because the API and the worker write to the same database concurrently — Postgres handles that without SQLite's single-writer locking — and over SQL Server for a lighter, faster image. EF Core migrations are provider-specific, so the provider is fixed before scaffolding.
- **App LLM: local model via Ollama (a docker-compose service), not a paid API.** The C# worker calls Ollama over HTTP; recommended abstraction is `Microsoft.Extensions.AI` (`IChatClient`) with the Ollama provider as the default, swappable to OpenAI/Gemini via config. No API keys required, runs with a single `docker compose up` (matches DoD's "local model" option + "start with one command"). Model name and base URL come from environment variables.
- **Orchestration: docker-compose** runs the whole system (backend API + worker + PostgreSQL + Ollama + static frontend) with one command. The compose file is created with the project scaffold.

## Conventions

- Project documentation may be written in Polish; keep DoD.md as-is and treat it as the requirements source.
