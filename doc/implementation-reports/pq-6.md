# Raport implementacji: pq-6 — Orkiestracja (docker-compose) + dokumentacja

> Projekt: [doc/projects/pq-6.md](../projects/pq-6.md)
> Data: 2026-07-15 (1 iteracja: implementacja + CR + realna weryfikacja `docker compose up`)
> Commit: `3490325 [pq-6] Orkiestracja: pełny docker compose + README` (+ follow-up: raport, CR-owe poprawki, dowód weryfikacji)
> MR: —
> Code review: 1× code-reviewer (**AKCEPTUJ** — 0 krytycznych, 0 ostrzeżeń, 7 sugestii kosmetycznych; 4 naniesione). Dodatkowo: pełna weryfikacja end-to-end na żywym `docker compose up` (nie tylko statyczny przegląd) — patrz [doc/verification-reports/pq-6.md](../verification-reports/pq-6.md).

## Co realizuje task

Spina pięć zaimplementowanych warstw (Postgres, Ollama, Api, Worker, frontend) w jedno polecenie `docker compose up`. Dokłada usługi `api`/`worker`/`frontend`/`ollama-pull` (auto-pull modelu) do istniejących `postgres`/`ollama`, Dockerfile'e (multi-stage .NET, node→nginx), reverse-proxy nginx (`/api` → Api, jeden origin, zero prod-CORS), oraz README. Jedyna zmiana kodu C#: dwuliniowa poprawka precedencji konfiguracji w obu `Program.cs` (bez niej Worker w kontenerze nie łączyłby się z Ollamą — patrz Werdykt CR/Wątki).

## Stan implementacji vs. projekt (v2)

| Decyzja projektu | Stan |
|------------------|------|
| Port Postgresa `5433:5432` (wymóg użytkownika — konflikt z natywnym PG na hoście) | ✅ [docker-compose.yml](../../docker-compose.yml) |
| `AddEnvironmentVariables()` po `AddJsonFile` (Api + Worker) — fix precedencji configu | ✅ zweryfikowane na żywo (Worker połączył się z `ollama:11434`) |
| `backend/.dockerignore` (blokował build bez niego) | ✅ |
| Frontend: nginx statyczny build + proxy `/api`, nie Vite preview | ✅ [nginx.conf](../../frontend/nginx.conf) |
| Auto-pull modelu przez one-shot `ollama-pull` | ✅ zweryfikowane: `exit 0`, Worker czeka na `service_completed_successfully` |
| Health-gate gotowości schematu = `/health` Api, bez zmian logiki Workera | ✅ zweryfikowane: Worker startuje dopiero po `api healthy` |
| `restart: unless-stopped` na api/worker | ✅ |

## Zakres zmian

### Orkiestracja

| Plik | Op | Opis |
|------|----|------|
| [docker-compose.yml](../../docker-compose.yml) | MOD | +4 usługi (`api`, `worker`, `frontend`, `ollama-pull`), port Postgresa → 5433, `${VAR:-default}` |
| [backend/.dockerignore](../../backend/.dockerignore) | NEW | `**/bin`, `**/obj`, `**/*.user`, `.vs/` |
| [backend/src/PromptQueue.Api/Dockerfile](../../backend/src/PromptQueue.Api/Dockerfile) | NEW | Multi-stage SDK→aspnet, `curl` dla healthchecku |
| [backend/src/PromptQueue.Worker/Dockerfile](../../backend/src/PromptQueue.Worker/Dockerfile) | NEW | Multi-stage SDK→runtime (bez Kestrela) |
| [frontend/Dockerfile](../../frontend/Dockerfile) | NEW | node:20 build → nginx:1.27 serve |
| [frontend/nginx.conf](../../frontend/nginx.conf) | NEW | SPA fallback + reverse-proxy `/api` → `api:8080` |
| [frontend/.dockerignore](../../frontend/.dockerignore) | NEW | `node_modules`, `dist` |
| [.env.example](../../.env.example) | NEW | `OLLAMA_MODEL`, poświadczenia dev Postgresa |
| [README.md](../../README.md) | MOD | Wymagania, `up`, porty (Postgres 5433), komponenty, dev-osobno, override modelu |

### Backend (jedyna zmiana kodu)

| Plik | Op | Opis |
|------|----|------|
| [backend/src/PromptQueue.Api/Program.cs](../../backend/src/PromptQueue.Api/Program.cs) | MOD | + `AddEnvironmentVariables()` po `AddJsonFile` |
| [backend/src/PromptQueue.Worker/Program.cs](../../backend/src/PromptQueue.Worker/Program.cs) | MOD | + `AddEnvironmentVariables()` po `AddJsonFile` |

Brak nowych testów (task infrastrukturalny) — istniejące 52 backendu + 23 frontu pozostają zielone, niezmienione.

## Wyniki weryfikacji

`dotnet build` (po edycji Program.cs): **0/0**. `dotnet test`: **52/52 zielone** (niezmienione tym taskiem).

`docker compose build`: wszystkie 3 obrazy (api/worker/frontend) zbudowane bez błędu.

**Pełna weryfikacja end-to-end na żywym stosie** (nie testy jednostkowe) — szczegóły, logi i zrzuty ekranu: [doc/verification-reports/pq-6.md](../verification-reports/pq-6.md). Skrót:
- Kolejność startu respektowana (Worker czeka na `api healthy` + `ollama-pull completed`); log Workera: „Model endpoint is ready." — potwierdza połączenie z `ollama:11434` (dowód działania fixu precedencji configu).
- Happy path: prompt przeszedł `pending→processing→completed` z realną odpowiedzią `llama3.2` przez frontend na `:8080` (nginx-proxy, jeden origin).
- Ścieżka błędu: zatrzymanie `ollama` → nowy prompt → `Failed` z komunikatem, Worker nie padł.

## Werdykt CR

**AKCEPTUJ** — 0 krytycznych, 0 ostrzeżeń, 7 sugestii (4 naniesione, 3 pominięte jako nice-to-have).

| # | Plik | Opis | Status |
|---|------|------|--------|
| S1 | [backend/.dockerignore](../../backend/.dockerignore) | Brak `.vs/` (generuje się w `backend/`, kontekst builda) | ✅ naniesione |
| S2 | [README.md](../../README.md) | `$env:...` (PowerShell) bez wariantu dla Linux/macOS | ✅ naniesione (`export`) |
| S3 | [README.md](../../README.md) | `all-test` (user-specific shortcut) zamiast przenośnego `dotnet test` | ✅ naniesione |
| S4 | [README.md](../../README.md) | Brak zdania o zmianie hasła Postgresa poza dev | ✅ naniesione |
| S5 | nginx.conf | Brak gzip/nagłówków bezpieczeństwa | ⏳ pominięte: nice-to-have, poza zakresem demo |
| S6 | docker-compose.yml | `frontend` bez `restart:` | ⏳ pominięte: nginx bezstanowy, świadome |
| S7 | Program.cs | `AddEnvironmentVariables` po `AddJsonFile` cieniuje ewentualne command-line args | ⏳ pominięte: projekt nie przekazuje configu przez args |

## Wątki do uwagi przy CR

1. **Fix precedencji konfiguracji był BLOKEREM znalezionym przez solution-critic, nie przewidzianym w pierwotnym planie „zero zmian kodu".** `backend/appsettings.json` (pq-1) zaszywał na stałe `Worker:OllamaBaseUrl: http://localhost:11434`; ręczny `AddJsonFile` (pq-1/pq-3) dopisywał ten plik **po** domyślnym źródle env vars w `IConfigurationBuilder`, więc w kontenerze env z compose (`Worker__OllamaBaseUrl=http://ollama:11434`) przegrywał. Naprawione dwuliniowo (`AddEnvironmentVariables()` po `AddJsonFile`, symetrycznie w Api i Workerze) i **potwierdzone empirycznie** — bez tego fixu Worker wisiałby w `WaitForModelAsync` w nieskończoność, łamiąc DoD po cichu (bez błędu buildu, dopiero w runtime kontenera).
2. **`docker network prune`** z wcześniejszej sesji weryfikacyjnej (pq-5) omyłkowo usunął sieci niepowiązanych projektów na tej maszynie — odnotowane wtedy, nie dotyczy pq-6, ale wspominam dla ciągłości: przy tej weryfikacji użyto wyłącznie nazwanych `docker compose down` (bez global prune), by uniknąć powtórki.

## Follow-up (poza scope)

- **nginx gzip/security headers** (S5) — jeśli front miałby być wystawiony poza lokalne demo.
- **`restart: unless-stopped` na frontend** (S6) — spójność, niski priorytet.
- Handoffy z pq-1..pq-3 (indeks `(Status,CreatedAt)`, token współbieżności `xmin`, retry z licznikiem trwałym) pozostają odłożone — poza zakresem orkiestracji.

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | Pełna implementacja (compose 6 usług, 3 Dockerfile'e, nginx, README, fix precedencji configu, `.dockerignore`) + realna weryfikacja `docker compose up` (happy path + ścieżka błędu, zrzuty ekranu) + naniesienie 4/7 sugestii CR | ✅ AKCEPTUJ |
