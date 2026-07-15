# PromptQueue

System do zgłaszania wielu promptów LLM naraz i śledzenia ich przetwarzania: `pending → processing → completed/failed`. Backend w C#/.NET, worker przetwarzający prompty przez lokalny model (Ollama), frontend w React.

## Wymagania

- **Docker** + **Docker Compose** (v2, wsparcie dla `depends_on: condition`).
- Wolne miejsce na dysku: **~2 GB** na pobranie modelu `llama3.2` (pierwszy start), plus obrazy kontenerów.
- Wolne porty na hoście: `8080`, `5269`, `5433`, `11434` (patrz niżej — Postgres celowo na `5433`, nie `5432`).

## Uruchomienie

```bash
docker compose up
```

To wszystko. Pierwsze uruchomienie potrwa dłużej (build obrazów + pobranie modelu ~2 GB przez `ollama-pull`); kolejne `docker compose up` są szybkie (wolumeny cache'ują dane i model).

Aplikacja: **http://localhost:8080**

Zatrzymanie: `docker compose down` (dane i model zostają w wolumenach). Pełny reset: `docker compose down -v`.

## Porty i adresy

| Usługa | Host URL | Uwagi |
|---|---|---|
| **Frontend** | http://localhost:8080 | Główny punkt wejścia; UI + proxy `/api` do Api |
| **Api** | http://localhost:5269 | Bezpośredni dostęp (debug/curl); frontend woła go przez proxy, nie bezpośrednio |
| **Postgres** | localhost:**5433** | **Nie 5432** — na wielu maszynach dev port 5432 zajmuje natywny PostgreSQL. Kontener wewnątrz sieci compose nadal używa 5432 |
| **Ollama** | http://localhost:11434 | API modelu; front/Api go nie widzą bezpośrednio (tylko worker, wewnątrz sieci) |

## Komponenty

- **postgres** — PostgreSQL 18, baza `promptqueue`. Schemat tworzy/migruje wyłącznie Api na starcie.
- **ollama** + **ollama-pull** — lokalny silnik LLM; `ollama-pull` to jednorazowy kontener pobierający model (`OLLAMA_MODEL`, domyślnie `llama3.2`) do wspólnego wolumenu, kończy się po pobraniu.
- **api** (ASP.NET Core) — `POST/GET /api/v1/prompts`, migracja bazy na starcie, `GET /health`.
- **worker** (.NET Generic Host) — odpytuje prompty `Pending`, woła Ollamę, zapisuje wynik/błąd. Startuje dopiero gdy Api ma zmigrowany schemat i model jest pobrany.
- **frontend** (React + Vite, serwowany przez nginx) — formularz dodawania promptów + lista z automatycznym odświeżaniem statusów.

## Zmienne środowiskowe

Skopiuj `.env.example` do `.env`, jeśli chcesz nadpisać wartości domyślne:

```bash
cp .env.example .env
```

| Zmienna | Domyślnie | Opis |
|---|---|---|
| `OLLAMA_MODEL` | `llama3.2` | Model pobierany przez `ollama-pull` i używany przez workera |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | `postgres` / `postgres` / `promptqueue` | Poświadczenia dev bazy |

Domyślne poświadczenia są jawnymi wartościami dev — na środowisku innym niż lokalne demo nadpisz `POSTGRES_PASSWORD` w `.env`.

## Pobranie modelu ręcznie (alternatywa dla auto-pull)

`ollama-pull` pobiera model automatycznie przy pierwszym starcie. Jeśli wolisz pobrać ręcznie (np. inny model niż domyślny, bez czekania na `ollama-pull`):

```bash
docker compose exec ollama ollama pull llama3.2
```

## Uruchamianie komponentów osobno (dev)

Do pracy nad pojedynczą warstwą bez pełnego `docker compose up`:

**Backend** — Postgres i Ollama z compose, Api/Worker lokalnie:
```powershell
docker compose up -d postgres ollama
$env:ConnectionStrings__PromptQueue = "Host=localhost;Port=5433;Database=promptqueue;Username=postgres;Password=postgres"
dotnet run --project backend/src/PromptQueue.Api
dotnet run --project backend/src/PromptQueue.Worker
```
Na Linux/macOS zamiast `$env:...` użyj `export ConnectionStrings__PromptQueue="Host=localhost;Port=5433;..."`.

Szczegóły: [doc/implementation-reports/pq-1.md](doc/implementation-reports/pq-1.md), [pq-3.md](doc/implementation-reports/pq-3.md).

**Frontend** — Vite dev server z proxy na lokalne Api (`:5269`):
```bash
cd frontend
npm install
npm run dev
```
Szczegóły: [doc/implementation-reports/pq-4.md](doc/implementation-reports/pq-4.md), [pq-5.md](doc/implementation-reports/pq-5.md).

**Testy backendu** (wymaga Dockera — Testcontainers): `dotnet test` z katalogu `backend/`.
**Testy frontendu**: `npm run test` z katalogu `frontend/`.

## Architektura

Szczegółowe projekty techniczne każdego etapu: [doc/projects/](doc/projects/) (pq-1 fundament/baza, pq-2 API, pq-3 worker/Ollama, pq-4/pq-5 frontend, pq-6 ta orkiestracja). Wymagania źródłowe: [doc/prepare-projects/DoD.md](doc/prepare-projects/DoD.md).
