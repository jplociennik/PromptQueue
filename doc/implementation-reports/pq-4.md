# Raport implementacji: pq-4 — Frontend: setup + dodawanie promptów

> Projekt: [doc/projects/pq-4.md](../projects/pq-4.md)
> Data: 2026-07-15 (TDD Red→Green + CR + iteracje: axios, struktura, poprawki CR)
> Commit: — (niezacommitowane; branch `pq/pq-3/main`)
> MR: —
> Code review: 1× code-reviewer (**AKCEPTUJ Z UWAGAMI**, na stanie axios+foldery). Naprawy W1/W2 oraz dalsza restruktura (hooki/`client` per-folder) naniesione po CR i **zweryfikowane self-review** (build 0, testy 13/13; zmiany = dokładnie rekomendacje reviewera).

## Co realizuje task

Fundament frontendu (Vite + React + TS SPA) i pierwsza ścieżka użytkownika: formularz dodawania wielu promptów przez `POST /api/v1/prompts` z walidacją pustych i feedbackiem sukces/błąd/ładowanie. Warstwa `api/` (axios) + typy współdzielone + struktura katalogów gotowe pod pq-5 (lista/polling — `getPrompts` przez `http.get`) i pq-6 (serwowanie — `VITE_API_BASE_URL`). Lista/polling, compose/nginx, prod-CORS — poza zakresem.

## Stan implementacji vs. projekt (v2 + decyzje wdrożeniowe)

| Decyzja | Stan |
|---------|------|
| Vite+React+TS strict; przypięte pod Node 20.11 | ✅ Vite 6.4.3, React 18.3.1, TS 5.6.3, Vitest 3.2.7 (nie Vite 7 — niekompat.) |
| Warstwy `ui ← features ← (hooks, api)`, klient jako jedyna brama HTTP | ✅ |
| **Klient HTTP: axios** (decyzja użytkownika, wbrew pierwotnemu `fetch`) | ✅ [client.ts](../../frontend/src/api/client/client.ts) — `http` + interceptor + `toApiError` |
| CSS Modules (separacja stylów); chat-input `TextArea` | ✅ `*.module.css`; `field-sizing: content` + fallback |
| **Folder-per-component/moduł** (komponent+css, hook/klient+test) | ✅ `components/UI/X/`, `hooks/X/`, `api/client/` |
| **Wszystkie hooki w `hooks/`** (korekta podziału data/form-hook) | ✅ `useCreatePrompts`, `usePromptFields` |
| Reset formularza tylko po sukcesie; blokada double-submit | ✅ `submit: Promise<boolean>`, `disabled` przy `submitting` |
| Łączność: dev-proxy Vite **+** dev-only CORS w Api | ✅ [Program.cs](../../backend/src/PromptQueue.Api/Program.cs) (`IsDevelopment()`) |
| Testy lekkie (logika, nie framework), Vitest+RTL | ✅ 13 testów: `toApiError`, `promptFieldsReducer`, `useCreatePrompts` |
| Typy = lustro kontraktu pq-2 (camelCase, ProblemDetails `errors`) | ✅ [types.ts](../../frontend/src/api/types.ts) |

## Zakres zmian

### Frontend (`frontend/`, nowy projekt)

| Plik | Op | Opis |
|------|----|------|
| Setup: `package.json`, `vite.config.ts`, `tsconfig*.json`, `index.html`, `.env.example`, `.gitignore`, `src/{main.tsx,vite-env.d.ts,setupTests.ts,index.css}` | NEW | Scaffold Vite react-ts (ręcznie, wersje przypięte); `defineConfig` z `vitest/config`, `server.proxy /api → :5269`, blok `test` (jsdom) |
| [api/types.ts](../../frontend/src/api/types.ts) | NEW | DTO (`CreatePromptsRequest/Response`, `PromptResponse`), `PromptStatus`, `ValidationErrors` |
| [api/client/client.ts](../../frontend/src/api/client/client.ts) | NEW | axios `http` + `ApiError` + czysta `toApiError` + response-interceptor |
| [api/prompts.ts](../../frontend/src/api/prompts.ts) | NEW | `createPrompts` przez `http.post` |
| [hooks/useCreatePrompts/useCreatePrompts.ts](../../frontend/src/hooks/useCreatePrompts/useCreatePrompts.ts) | NEW | Cykl wysyłki (`useReducer`), `submit: Promise<boolean>` |
| [hooks/usePromptFields/usePromptFields.ts](../../frontend/src/hooks/usePromptFields/usePromptFields.ts) | NEW | `promptFieldsReducer` (add/remove/change/reset, ≥1 pole) + `isBlank` + hook |
| [features/prompts/types.ts](../../frontend/src/features/prompts/types.ts) | NEW | `PromptField` |
| [features/prompts/PromptForm/PromptForm.tsx](../../frontend/src/features/prompts/PromptForm/PromptForm.tsx) (+`.module.css`) | NEW | Formularz: walidacja pustych, reset po sukcesie, blokada double-submit, `Alert` z `validationErrors` |
| [features/prompts/PromptField/PromptField.tsx](../../frontend/src/features/prompts/PromptField/PromptField.tsx) (+`.module.css`) | NEW | Pole: `TextArea` chat-input, `aria-label`, „Usuń" `disabled` przy jednym polu |
| `components/UI/{Button,TextArea,Alert}/*.tsx` (+`.module.css`) | NEW | Reużywalne UI |
| `App.tsx` | NEW | Composition root (nagłówek + `PromptForm`) |

### Backend (jedyna zmiana)

| Plik | Op | Opis |
|------|----|------|
| [Program.cs](../../backend/src/PromptQueue.Api/Program.cs) | MOD | `AddCors()` + dev-only `UseCors(AllowAny…)` po `UseExceptionHandler` (prod-CORS → pq-6) |

### Testy

| Plik | Op | Pokrycie |
|------|----|----------|
| [api/client/client.test.ts](../../frontend/src/api/client/client.test.ts) | NEW | `toApiError` (3): 400+`errors`, 500, błąd sieci (status 0) |
| [hooks/usePromptFields/usePromptFields.test.ts](../../frontend/src/hooks/usePromptFields/usePromptFields.test.ts) | NEW | reducer+`isBlank` (7): add/remove/change/reset, remove ostatniego pola → ≥1, blank |
| [hooks/useCreatePrompts/useCreatePrompts.test.ts](../../frontend/src/hooks/useCreatePrompts/useCreatePrompts.test.ts) | NEW | (3): sukces→true, `ApiError`→false+validationErrors, non-ApiError→false+fallback |

## Wyniki testów

| Projekt | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| frontend (Vitest) | 13 | 0 | 0 |
| PromptQueue.Api.IntegrationTests (backend, po dev-CORS) | 7 | 0 | 0 |

`npm run build` (tsc + vite): **0 błędów**. `dotnet build backend/PromptQueue.sln`: **0/0**. Testy backendu Api.IntegrationTests potwierdziły, że dev-CORS nie zepsuł potoku (middleware bezczynny bez `Origin`).

## Werdykt CR (po przeglądzie + naprawach)

**AKCEPTUJ Z UWAGAMI** — 0 krytycznych, 3 ostrzeżenia (2 naprawione, 1 doc-sync), sugestie naniesione.

| # | Plik | Opis | Status |
|---|------|------|--------|
| W1 | [usePromptFields.ts](../../frontend/src/hooks/usePromptFields/usePromptFields.ts), [PromptForm.tsx](../../frontend/src/features/prompts/PromptForm/PromptForm.tsx) | Usunięcie ostatniego pola → pusty POST → 400 | ✅ naprawione: `remove` nie usuwa ostatniego + „Usuń" disabled przy 1 polu + test reducera |
| W2 | [PromptForm.tsx](../../frontend/src/features/prompts/PromptForm/PromptForm.tsx) | `validationErrors` łapane, ale nierenderowane (user widział generyczny ang. `title`) | ✅ naprawione: spłaszczone `validationErrors` w `Alert` |
| W3 | [doc/projects/pq-4.md](../projects/pq-4.md) | Doc mówił `fetch`, kod ma axios | ✅ zsynchronizowany projekt (struktura + axios + decyzje) |
| S | prompts.ts / PromptField / useCreatePrompts.test | `satisfies CreatePromptsRequest`, `aria-label` „Usuń", test non-ApiError fallback | ✅ naniesione |

## Wątki do uwagi przy CR (świadome decyzje)

1. **axios zamiast fetch** ([client.ts](../../frontend/src/api/client/client.ts)) — decyzja użytkownika wbrew pierwotnemu projektowi (który używał `fetch`+`apiFetch`). Dodaje zależność runtime, ale zgodna ze stackiem użytkownika (react-course) i ubocznie usuwa handoff „bodyless GET". `ApiError`/kontrakt zachowane; interceptor mapuje błędy.
2. **Konwencja folder-per-moduł + hooki w `hooks/`** — dwie iteracje strukturalne po feedbacku użytkownika (skill `code-frontend` reguła 9). Pierwotny podział „form-hook przy feature" wycofany (mismatch nazwa↔lokalizacja).
3. **dev-only CORS w `Program.cs`** — jedyna zmiana backendu; redundantny z dev-proxy (świadome „pas + szelki"), bramkowany `IsDevelopment()`, bez wpływu na prod/testy.

## Follow-up (poza scope)

- **Handoff pq-5**: `getPrompts` przez `http.get<PromptResponse[]>` (typ już zadeklarowany); `usePromptPolling`/`usePrompts` w `hooks/` (folder-per-hook); przy testach renderujących komponenty dodać `afterEach(cleanup)` lub `globals: true` (teraz `globals: false`).
- **Handoff pq-6**: `crypto.randomUUID()` wymaga secure context (localhost/https) — udokumentować lub fallback przy serwowaniu po http na LAN.
- **i18n (sugestia)**: komunikaty PL bez pluralizacji („Dodano promptów: {count}"); pełna pluralizacja odłożona.

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | TDD: Red (scaffold Vite + stuby + 3 testy → 11 red) → Green (logika `client`/reducer/`useCreatePrompts` + komponenty + CSS chat-input + dev-CORS → 11/11) | — |
| 2 | axios zamiast fetch (`http`+interceptor+`toApiError`) + restruktura folder-per-component; `usePromptFields` → `hooks/` | ✅ AKCEPTUJ Z UWAGAMI |
| 3 | Naprawy CR (W1 pusta lista, W2 render `validationErrors`) + hooki/`client` per-folder + drobne (satisfies, aria-label, test fallback) → 13/13 | — (self-review) |
