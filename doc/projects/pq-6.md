# Projekt: pq-6 — Orkiestracja (docker-compose) + dokumentacja

> Data: 2026-07-15
> Wersja: 2
> Werdykt: AKCEPTUJ (v1 = PRZEPROJEKTUJ wąsko, 2 blokery naprawione i zweryfikowane wobec kodu; reszta była już poprawna)

## Cel

Spiąć pięć zaimplementowanych warstw (Postgres, Ollama, Api, Worker, frontend) w jedno polecenie `docker compose up` i dołączyć README. DoD: `docker compose up` stawia całość; UI działa i rozmawia z Api; Worker przetwarza prompty przez Ollamę; README pozwala odtworzyć środowisko od zera. Źródło: [pq-6 → L21](../prepare-projects/pq-6.md#L21), [DoD → L9](../prepare-projects/DoD.md#L9).

Compose rośnie przyrostowo: `postgres` istnieje od pq-1, `ollama` od pq-3 — tu dokładamy `api`, `worker`, `frontend`, init-pull modelu, Dockerfile'e i README. **Wymaga dwóch punktowych poprawek w istniejącym kodzie** (Program.cs Api+Worker — patrz § Kluczowe decyzje, poprawka precedencji konfiguracji) — reszta pq-1..pq-5 nietknięta.

## Proponowana architektura

Sześć usług w domyślnej sieci compose. Krawędzie = zależności startowe (`depends_on` z warunkiem). Host-porty po lewej, wewnątrz sieci usługi wołają się po nazwie.

```
                      ┌──────────────────────── docker compose network ────────────────────────┐
  host:8080  ─────────►  frontend (nginx)                                                       │
                      │     │  serwuje SPA (static)                                              │
                      │     └─ /api/* ──reverse-proxy──► api:8080                                │
  host:5269  ─────────►  api (ASP.NET) :8080  ──► postgres:5432                                  │
                      │     migruje schemat na starcie, potem /health=200                        │
                      │        ▲ (schema ready)          ▲                                       │
                      │        │                         │                                       │
                      │     worker  ──► postgres:5432    │   ──► ollama:11434                    │
                      │        │  polling Pending → LLM → Completed/Failed                       │
  host:5433 ─────────►  postgres :5432  (wolumen postgres-data)                                  │
  host:11434 ────────►  ollama :11434  (wolumen ollama-data)                                     │
                      │        ▲                                                                 │
                      │     ollama-pull (one-shot: ollama pull llama3.2 → exit)                  │
                      └────────────────────────────────────────────────────────────────────────┘

Bramki startu:  postgres(healthy) → api(healthy=schema) → worker
                ollama(healthy) → ollama-pull(completed) → worker
                api(healthy) → frontend
```

### docker-compose.yml (kompletny)

```yaml
services:
  postgres:
    image: postgres:18-alpine
    container_name: promptqueue-postgres
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-postgres}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-postgres}
      POSTGRES_DB: ${POSTGRES_DB:-promptqueue}
    ports:
      - "5433:5432"
    volumes:
      - postgres-data:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-postgres} -d ${POSTGRES_DB:-promptqueue}"]
      interval: 10s
      timeout: 5s
      retries: 5

  ollama:
    image: ollama/ollama:0.32.0
    container_name: promptqueue-ollama
    ports:
      - "11434:11434"
    volumes:
      - ollama-data:/root/.ollama
    healthcheck:
      test: ["CMD", "ollama", "list"]
      interval: 10s
      timeout: 5s
      retries: 5

  ollama-pull:
    image: ollama/ollama:0.32.0
    container_name: promptqueue-ollama-pull
    depends_on:
      ollama:
        condition: service_healthy
    environment:
      OLLAMA_HOST: http://ollama:11434
    entrypoint: ["/bin/sh", "-c"]
    command: ["ollama pull ${OLLAMA_MODEL:-llama3.2}"]
    restart: "no"

  api:
    build:
      context: ./backend
      dockerfile: src/PromptQueue.Api/Dockerfile
    container_name: promptqueue-api
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__PromptQueue: "Host=postgres;Port=5432;Database=${POSTGRES_DB:-promptqueue};Username=${POSTGRES_USER:-postgres};Password=${POSTGRES_PASSWORD:-postgres}"
    ports:
      - "5269:8080"
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:8080/health || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 40s

  worker:
    build:
      context: ./backend
      dockerfile: src/PromptQueue.Worker/Dockerfile
    container_name: promptqueue-worker
    restart: unless-stopped
    depends_on:
      api:
        condition: service_healthy
      ollama-pull:
        condition: service_completed_successfully
    environment:
      DOTNET_ENVIRONMENT: Production
      ConnectionStrings__PromptQueue: "Host=postgres;Port=5432;Database=${POSTGRES_DB:-promptqueue};Username=${POSTGRES_USER:-postgres};Password=${POSTGRES_PASSWORD:-postgres}"
      Worker__OllamaBaseUrl: http://ollama:11434
      Worker__OllamaModel: ${OLLAMA_MODEL:-llama3.2}

  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    container_name: promptqueue-frontend
    depends_on:
      api:
        condition: service_healthy
    ports:
      - "8080:80"

volumes:
  postgres-data:
  ollama-data:
```

### Poprawka kodu — precedencja konfiguracji (BLOKER v1, naprawione)

`Host.CreateApplicationBuilder`/`WebApplication.CreateBuilder` dodają domyślnie źródła w kolejności `appsettings.json → appsettings.{env}.json → user secrets (dev) → env vars → args`; **ostatnie źródło wygrywa**. Oba `Program.cs` dopisują współdzielony `backend/appsettings.json` ręcznie **po** budowie buildera — czyli **po** domyślnym źródle env vars — więc `Worker:OllamaBaseUrl`/`Worker:OllamaModel` z `appsettings.json` (`http://localhost:11434`, na stałe) **nadpisywały** `Worker__OllamaBaseUrl`/`Worker__OllamaModel` z compose. W kontenerze Worker próbowałby `localhost:11434` (nieosiągalny) i nigdy nic by nie przetworzył — DoD złamane bez zmiany kodu. Fix: przywrócić precedencję env, dopisując `AddEnvironmentVariables()` **po** ręcznym `AddJsonFile` w obu plikach (Api symetrycznie, na przyszłość — dziś Api nie ma odpowiednika `Worker`-owej sekcji nadpisywanej przez appsettings, ale connection string i tak jest env-only i nietknięty tym bugiem; poprawka defensywna):

```csharp
// Worker/Program.cs i Api/Program.cs, zaraz po istniejącym builder.Configuration.AddJsonFile(...):
builder.Configuration.AddEnvironmentVariables();
```

### backend/.dockerignore (BLOKER v1, naprawione)

Bez tego `COPY . .` w Dockerfile wciąga hostowe `**/obj` (zawierają `project.assets.json` z Windowsowymi ścieżkami NuGet z lokalnych `all-build`), nadpisując świeży restore kontenera — `dotnet publish --no-restore` pada. Katalogi `obj/` **istnieją już dziś na dysku** (potwierdzone) — bez tego pliku `docker compose up --build` nie przejdzie.

```
**/bin
**/obj
**/*.user
```

### backend/src/PromptQueue.Api/Dockerfile

Multi-stage; kontekst = `./backend` (linkowany `..\..\appsettings.json` musi być w kontekście — potwierdzone, `COPY . .` go obejmuje). `curl` doinstalowany dla healthchecku (`aspnet` Debian go nie zawiera).

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PromptQueue.Domain/PromptQueue.Domain.csproj src/PromptQueue.Domain/
COPY src/PromptQueue.Infrastructure/PromptQueue.Infrastructure.csproj src/PromptQueue.Infrastructure/
COPY src/PromptQueue.Api/PromptQueue.Api.csproj src/PromptQueue.Api/
RUN dotnet restore src/PromptQueue.Api/PromptQueue.Api.csproj
COPY . .
RUN dotnet publish src/PromptQueue.Api/PromptQueue.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PromptQueue.Api.dll"]
```

### backend/src/PromptQueue.Worker/Dockerfile

Base `runtime` (nie `aspnet`) — Worker to Generic Host bez Kestrela. Bez healthchecku (nic od Workera nie zależy).

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PromptQueue.Domain/PromptQueue.Domain.csproj src/PromptQueue.Domain/
COPY src/PromptQueue.Infrastructure/PromptQueue.Infrastructure.csproj src/PromptQueue.Infrastructure/
COPY src/PromptQueue.Worker/PromptQueue.Worker.csproj src/PromptQueue.Worker/
RUN dotnet restore src/PromptQueue.Worker/PromptQueue.Worker.csproj
COPY . .
RUN dotnet publish src/PromptQueue.Worker/PromptQueue.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PromptQueue.Worker.dll"]
```

### frontend/Dockerfile

Node build → nginx serve. Node pinowany 20 (pq-4: przypięte pod Node 20.11).

```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
```

### frontend/nginx.conf

SPA fallback + reverse-proxy `/api` → `api:8080`. `proxy_pass` **bez** URI po `http://api:8080` przekazuje pełną ścieżkę żądania (`/api/v1/prompts` trafia do `api:8080/api/v1/prompts`, dokładnie tam gdzie backend go mapuje — zweryfikowane). Jeden origin → `VITE_API_BASE_URL` puste, prod-CORS niepotrzebny.

```nginx
server {
  listen 80;

  location /api/ {
    proxy_pass http://api:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
  }

  location / {
    root /usr/share/nginx/html;
    try_files $uri $uri/ /index.html;
  }
}
```

### .env.example (root, nowy)

```dotenv
# Model LLM pobierany przez ollama-pull i używany przez workera
OLLAMA_MODEL=llama3.2

# Poświadczenia bazy (dev) — używane przez postgres oraz w connection stringach api/worker
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres
POSTGRES_DB=promptqueue
```

### frontend/.dockerignore (nowy)

```
node_modules
dist
```

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---|---|---|
| `builder.Configuration.AddEnvironmentVariables()` po `AddJsonFile` | Backend (MOD) | `backend/src/PromptQueue.Worker/Program.cs`, `backend/src/PromptQueue.Api/Program.cs` |
| `**/bin`, `**/obj`, `**/*.user` | Orkiestracja (NEW) | `backend/.dockerignore` |
| Usługi `api`, `worker`, `frontend`, `ollama-pull`; port `5433:5432`; env; `depends_on` z warunkami; `restart: unless-stopped` na api/worker | Orkiestracja (MOD) | `docker-compose.yml` |
| Dockerfile Api (multi-stage + curl) | Orkiestracja (NEW) | `backend/src/PromptQueue.Api/Dockerfile` |
| Dockerfile Worker (multi-stage, runtime) | Orkiestracja (NEW) | `backend/src/PromptQueue.Worker/Dockerfile` |
| Dockerfile frontend (node build → nginx) | Orkiestracja (NEW) | `frontend/Dockerfile` |
| `nginx.conf` (SPA fallback + proxy `/api`) | Orkiestracja (NEW) | `frontend/nginx.conf` |
| `.dockerignore` frontendu | Orkiestracja (NEW) | `frontend/.dockerignore` |
| `.env.example` (model, poświadczenia dev) | Orkiestracja (NEW) | `.env.example` |
| README (wymagania, up, porty/URL, komponenty, dev-osobno) | Dokumentacja (MOD) | `README.md` (dziś placeholder 13 B) |

Brak nowych migracji. Testy backendu (52) nietknięte — `AddEnvironmentVariables()` nie zmienia zachowania w `dotnet run`/testach (env i tak tam dominuje inaczej niż w Docker — poprawka jest defensywna/symetryczna, realny bug ujawniał się tylko w kontenerze).

## Przepływ danych / startu

Kod przepływu żądań opisany w pq-2 (POST/GET) i pq-3 (pętla Workera) — tu wyłącznie kolejność bootstrapu, której `depends_on` nie pokazuje wprost:

1. **postgres** i **ollama** startują równolegle; healthchecki (`pg_isready`, `ollama list`) → `healthy`.
2. **ollama-pull** startuje po `ollama healthy`, wykonuje `ollama pull ${OLLAMA_MODEL}` na serwerze `ollama` (do wolumenu `ollama-data`), kończy się (`exit 0`) → `completed_successfully`.
3. **api** startuje po `postgres healthy`: `await app.Services.ApplyMigrationsAsync()` jest **awaitowane przed `app.Run()`** ([Program.cs:33,38](../../backend/src/PromptQueue.Api/Program.cs#L33-L38)) — Kestrel nie zaczyna nasłuchu, dopóki migracja się nie skończy, więc `/health` nie odpowie wcześniej. **Zatem `api healthy` ⟹ schemat gotowy** (nie dzięki kolejności `MapGet`, tylko dzięki temu, że migracja blokuje uruchomienie hosta).
4. **worker** startuje dopiero gdy `api healthy` (schemat) **i** `ollama-pull completed` (model obecny). Na starcie robi recovery `Processing→Pending`, `WaitForModelAsync` (szybko wraca — model już pobrany), potem polling. Dzięki poprawce precedencji env realnie łączy się z `ollama:11434`, nie `localhost:11434`.
5. **frontend** startuje po `api healthy`; nginx serwuje SPA i proxuje `/api` do `api:8080`.

Ścieżka gotowości schematu (handoff pq-1/pq-3) jest domknięta **bez dodatkowej logiki Workera** — healthcheck `/health` Api jest wystarczającą bramką. Worker nie implementuje wait-for-schema; jego `WaitForModelAsync` odpowiada wyłącznie za gotowość Ollamy.

Uwaga sieciowa: wewnątrz compose api/worker łączą się przez `postgres:5432` (port kontenera), niezależnie od host-mappingu `5433`. `Worker__OllamaBaseUrl=http://ollama:11434` i `ollama-pull` z `OLLAMA_HOST=http://ollama:11434` używają nazwy usługi, nie `localhost`.

## Kluczowe decyzje

- **[ROZSTRZYGNIĘTE — twardy wymóg użytkownika] Port Postgresa `5433:5432`.** Na maszynach dev natywny PostgreSQL zajmuje host-port 5432 (realny konflikt wykryty podczas manualnej weryfikacji pq-5). Zmienia się **wyłącznie** mapowanie host:container; kontener nasłuchuje wewnętrznie 5432, api/worker łączą się przez `postgres:5432`. Wszystkie host-side connection stringi (`dotnet ef database update`, `psql` z hosta) → port **5433** (README).
- **[NAPRAWIONE — BLOKER krytyki] `AddEnvironmentVariables()` po ręcznym `AddJsonFile`.** Zweryfikowane wobec realnego kodu ([Worker/Program.cs](../../backend/src/PromptQueue.Worker/Program.cs), [appsettings.json](../../backend/appsettings.json)): bez tej linii `Worker:OllamaBaseUrl`/`Worker:OllamaModel` na stałe zaszyte w współdzielonym appsettings.json nadpisywały env z compose → Worker w kontenerze nigdy nie łączyłby się z Ollamą. To jedyna zmiana kodu C# w pq-6; „zero zmian w kodzie" z pierwotnego planu okazało się nierealne.
- **[NAPRAWIONE — BLOKER krytyki] `backend/.dockerignore`.** Bez wykluczenia `**/obj`/`**/bin` build kontenera pada na `dotnet publish --no-restore`, bo `COPY . .` nadpisuje świeży restore hostowymi artefaktami (obecnymi na dysku po lokalnych `all-build`).
- **Frontend = nginx (statyczny build) + reverse-proxy `/api`, nie Vite preview.** Decyzja użytkownika (2026-07-15, przyjęta propozycja architekta). Vite preview to serwer deweloperski; compose ma charakter produkcyjny. nginx daje jeden origin (`VITE_API_BASE_URL` puste, [client.ts](../../frontend/src/api/client/client.ts)) → prod-CORS zbędny; Api-port publikowany tylko do debugu.
- **Auto-pull modelu przez one-shot `ollama-pull`.** Decyzja użytkownika (2026-07-15, przyjęta propozycja). Bez modelu Worker czekałby w `WaitForModelAsync` w nieskończoność — łamałoby DoD „jedno polecenie" out-of-the-box. Zweryfikowane: `service_completed_successfully` to poprawna, aktualna składnia Compose; model pobiera serwer `ollama` (wolumen `ollama-data`), `ollama-pull` jest tylko klientem-wyzwalaczem. Koszt: pierwszy `up` dłuższy o ~2 GB; kolejne natychmiastowe (wolumen). Ręczny `docker compose exec ollama ollama pull <model>` udokumentowany w README jako alternatywa.
- **Model domyślny `llama3.2`, porty front `8080`/Api `5269`/Postgres `5433`/Ollama `11434`.** Decyzja użytkownika (2026-07-15, przyjęte propozycje architekta bez zmian).
- **Gotowość schematu = healthcheck `/health` Api.** Zweryfikowane: gatuje `await ApplyMigrationsAsync()` przed `app.Run()` (nie kolejność `MapGet`) — Kestrel nie nasłuchuje do zakończenia migracji, więc `api healthy ⟹ schemat gotowy`. `worker depends_on api: service_healthy` wystarcza bez dodatkowej logiki.
- **Api healthcheck przez `curl` doinstalowany w obrazie runtime.** `aspnet:10.0` (Debian bookworm) nie ma `curl`; `apt-get install --no-install-recommends curl` zweryfikowane jako poprawna składnia. `/health` niezależny od DB. `start_period: 40s` z zapasem pokrywa boot + jedną migrację `InitialCreate` (&lt;1s, jedna tabela).
- **Worker na obrazie `runtime`, nie `aspnet`.** Generic Host + `HttpClient` do Ollamy — nie potrzebuje Kestrela; lżejszy obraz.
- **`restart: unless-stopped` na `api`/`worker`.** Odporność na restart Postgresa/Ollamy bez ręcznej interwencji (tanie, sensowne dla demo trwającego dłużej niż jedną sesję).
- **Kontekst build = `./backend` dla api/worker.** Zweryfikowane: wspólny `backend/appsettings.json` linkowany przez `..\..\appsettings.json` w csproj rozwiązuje się poprawnie w tym kontekście, `dotnet publish` kopiuje go do output.
- **Konfiguracja przez `.env` + `${VAR:-default}`.** Nadpisywalne: `OLLAMA_MODEL`, poświadczenia Postgresa. Connection string design-time (`dotnet ef`) pozostaje host-side env-only, teraz na porcie **5433**.

## Plan implementacji

1. Dopisz `builder.Configuration.AddEnvironmentVariables();` po istniejącym `AddJsonFile(...)` w `backend/src/PromptQueue.Worker/Program.cs` i `backend/src/PromptQueue.Api/Program.cs`.
2. Utwórz `backend/.dockerignore` (`**/bin`, `**/obj`, `**/*.user`).
3. Dodaj usługi `api`, `worker`, `frontend`, `ollama-pull` do `docker-compose.yml`; zmień `postgres.ports` na `5433:5432`; sparametryzuj env przez `${...}`; `restart: unless-stopped` na api/worker.
4. Utwórz `backend/src/PromptQueue.Api/Dockerfile` (multi-stage SDK→aspnet, `apt-get install curl`, publish Api).
5. Utwórz `backend/src/PromptQueue.Worker/Dockerfile` (multi-stage SDK→runtime, publish Worker).
6. Utwórz `frontend/Dockerfile` (node:20 build → nginx:1.27 serve) i `frontend/.dockerignore` (`node_modules`, `dist`).
7. Utwórz `frontend/nginx.conf` (SPA `try_files` + `location /api/ proxy_pass http://api:8080`).
8. Utwórz `.env.example` w root (OLLAMA_MODEL, POSTGRES_USER/PASSWORD/DB).
9. Napisz `README.md` (wymagania, `docker compose up`, porty/URL z Postgresem na 5433, opis komponentów, uruchamianie osobno w dev z linkami do raportów pq-1..pq-5, override modelu).
10. Weryfikacja DoD — patrz § Strategia (checklista `docker compose up` od zera, w tym potwierdzenie że build przechodzi i Worker realnie łączy się z `ollama:11434`).

## Strategia testowania / weryfikacji

Task infrastrukturalny — brak nowych testów jednostkowych/integracyjnych (istniejące 52 backendu + 23 frontu pozostają zielone, niezmienione tym taskiem). Weryfikacja **manualna**, checklista DoD (od czystego stanu: `docker compose down -v`):

1. **Build przechodzi:** `docker compose build` → wszystkie 3 obrazy (api/worker/frontend) budują się bez błędu (w szczególności `dotnet publish --no-restore` w api/worker — dowód że `.dockerignore` działa).
2. **Jedno polecenie stawia całość:** `docker compose up` → wszystkie usługi wstają; `ollama-pull` kończy się `exit 0`; `api` loguje „Applying/Applied migrations"; `worker` loguje „PromptProcessingWorker started" + **„Model endpoint is ready"** (dowód że łączy się z `ollama:11434`, nie `localhost` — weryfikuje fix precedencji env).
3. **Kolejność/health-gate:** `worker` nie startuje przed `api healthy` i `ollama-pull completed`; brak w logach Workera błędów „relation prompts does not exist".
4. **Front działa i rozmawia z Api:** `http://localhost:8080` renderuje SPA; dodanie promptów (pq-4) → `POST /api/v1/prompts` przez nginx-proxy zwraca 200; lista (pq-5) pokazuje wpisy i odświeża się pollingiem.
5. **Worker przetwarza:** dodany prompt przechodzi `pending → processing → completed` z wynikiem modelu.
6. **Ścieżka błędu:** `docker compose stop ollama` → nowy prompt kończy jako `failed` z komunikatem, Worker żyje.
7. **Persystencja:** `docker compose down && docker compose up` (bez `-v`) → dane i model przetrwały, drugi start bez ponownego pobierania.
8. **Port 5433:** połączenie z hosta na 5433 działa, brak konfliktu z ew. natywnym PG na 5432.
9. **Odtwarzalność README:** przejście instrukcji krok-po-kroku na czystej maszynie stawia środowisko od zera.

README dokumentuje też uruchamianie osobne w dev (Postgres+Ollama z compose, `dotnet run` Api/Worker, `npm run dev` frontu) — connection string host-side na porcie **5433**.

## Pytania do użytkownika

Brak otwartych pytań — stylowanie frontu (nginx), pobranie modelu (auto-pull), nazwa modelu (`llama3.2`) i porty rozstrzygnięte decyzją użytkownika z 2026-07-15 (przyjęte propozycje architekta, patrz § Kluczowe decyzje).

## Krytyka (solution-critic)

**v1 — Werdykt: PRZEPROJEKTUJ** (wąsko — 2 blokery, reszta poprawna). Potwierdzone jako poprawne bez zmian: `service_completed_successfully` (aktualna składnia Compose, `ollama-pull` działa jak init-container); multi-stage restore (Domain+Infrastructure+projekt w każdym Dockerfile, brak brakującego `.csproj`); nginx `proxy_pass` bez URI (zachowuje pełną ścieżkę `/api/v1/prompts`); `curl`/`apt-get` na `aspnet:10.0`; kontekst `./backend` obejmuje linkowany appsettings; porty i obrazy pinowane.

Blokery (naniesione w v2, zweryfikowane przeze mnie bezpośrednio w kodzie przed zapisem — bez dodatkowej rundy krytyki, ryzyko niskie przy tak precyzyjnie zlokalizowanych poprawkach):

| # | Waga | Uwaga | Stan |
|---|------|-------|------|
| 1 | BLOKER | `Worker:OllamaBaseUrl`/`OllamaModel` z appsettings.json nadpisują env compose (`AddJsonFile` po domyślnym źródle env) → Worker w kontenerze nigdy nie łączy się z Ollamą | ✅ naniesione: `AddEnvironmentVariables()` po `AddJsonFile` w obu Program.cs |
| 2 | BLOKER | Brak `backend/.dockerignore` → `COPY . .` wciąga hostowe `obj/` (istniejące na dysku) → `dotnet publish --no-restore` pada | ✅ naniesione: `backend/.dockerignore` |
| 3 | DROBNE | Uzasadnienie health-gate mylące („endpoint mapuje się po migracji" zamiast „await blokuje `app.Run()`") | ✅ poprawione w § Przepływ/Kluczowe decyzje |
| 4 | DROBNE | Brak `restart:` na api/worker | ✅ dodane `unless-stopped` |

## Historia wersji

- v1 (2026-07-15): projekt system-architect (6 usług compose, Dockerfile'e Api/Worker/frontend, nginx reverse-proxy, one-shot ollama-pull, health-gate, port Postgresa 5433).
- v2 (2026-07-15): naprawiono 2 blokery krytyki (precedencja konfiguracji env — jedyna zmiana kodu C# w tasku; brakujący `backend/.dockerignore`), poprawiono uzasadnienie health-gate, dodano `restart: unless-stopped`; decyzje użytkownika (nginx, auto-pull, `llama3.2`, porty) przyjęte jako propozycje architekta bez zmian.
