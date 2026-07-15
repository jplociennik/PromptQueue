# Projekt: pq-4 — Frontend: setup + dodawanie promptów

> Data: 2026-07-15
> Wersja: 2
> Werdykt: AKCEPTUJ (delta-krytyka v2 bez uwag istotnych; v1 = AKCEPTUJ Z UWAGAMI, wszystkie uwagi naniesione)

## Cel

Postawić cały fundament frontendu (Vite + React + TS SPA) i pierwszą ścieżkę użytkownika: formularz dodawania wielu promptów wysyłany przez `POST /api/v1/prompts`. Źródło: [DoD → L7](../prepare-projects/DoD.md#L7), [pq-4 → L13](../prepare-projects/pq-4.md#L13). Zależy od kontraktu [pq-2](pq-2.md) (POST). Fundament (warstwa `api/`, typy współdzielone, struktura, sposób łączenia) ma pozwolić [pq-5](../prepare-projects/pq-5.md) dokleić listę + polling, a [pq-6](../prepare-projects/pq-6.md) — serwowanie statyczne, **bez przeróbek**. Zakres: setup + formularz + minimalny dev-CORS w Api (§ Kluczowe decyzje). Poza zakresem: lista/polling (pq-5), compose/nginx/prod-CORS (pq-6).

## Proponowana architektura

Trzy warstwy z jednokierunkową zależnością `ui ← features ← (hooks, api)`: komponenty domenowe komponują reużywalne `components/ui`, logikę delegują do hooków, a jedyny punkt styku z HTTP to `api/`. Prompt bywa wielolinijkowy (formatowanie znaczące dla LLM — [pq-2 → L310](pq-2.md#L310)), więc kontrolką jest `TextArea` w stylu chat-input, a wpisywanie to **dynamiczne pola add/remove** (§ Kluczowe decyzje), mapowane 1:1 na kontrakt `prompts: string[]`. Jedyna zmiana backendu to dev-only polityka CORS w `Program.cs`.

Struktura finalna (konwencja code-frontend reguła 9 — moduł+towarzysz = własny folder; bez barreli):

```
frontend/
  index.html  package.json  tsconfig*.json  .env.example
  vite.config.ts                # defineConfig z 'vitest/config': react + server.proxy /api + test
  src/
    main.tsx  App.tsx  index.css  vite-env.d.ts  setupTests.ts
    api/
      types.ts                  # DTO + PromptStatus + ValidationErrors (płaski — bez testu)
      prompts.ts                # createPrompts przez http (pq-5 dokłada getPrompts) (płaski)
      client/  client.ts  client.test.ts       # axios: http + ApiError + toApiError
    hooks/                      # wszystkie custom hooki, folder-per-hook (moduł+test)
      useCreatePrompts/  useCreatePrompts.ts  useCreatePrompts.test.ts
      usePromptFields/   usePromptFields.ts   usePromptFields.test.ts   # promptFieldsReducer + isBlank
    features/prompts/
      types.ts                  # PromptField (płaski — typy)
      PromptForm/   PromptForm.tsx   PromptForm.module.css
      PromptField/  PromptField.tsx  PromptField.module.css
    components/UI/               # folder-per-component + współlokowany .module.css
      Button/    Button.tsx    Button.module.css
      TextArea/  TextArea.tsx  TextArea.module.css   # chat-input: field-sizing + fallback
      Alert/     Alert.tsx     Alert.module.css      # feedback success/error (reużywa pq-5)
```

### Kluczowe abstrakcje (kod)

Typy współdzielone — SSOT słownika statusów i kształtów DTO; `PromptResponse` **pre-deklarowany dla pq-5** (GET):

```ts
export type PromptStatus = 'pending' | 'processing' | 'completed' | 'failed';

export interface CreatePromptsRequest { prompts: string[]; }
export interface CreatePromptsResponse { ids: string[]; status: PromptStatus; }

export interface PromptResponse {
  id: string;
  content: string;
  status: PromptStatus;
  result: string | null;
  errorMessage: string | null;
  createdAt: string;
  updatedAt: string;
}

export type ValidationErrors = Record<string, string[]>;
```

Klient HTTP — **axios** (decyzja użytkownika, patrz § Kluczowe decyzje): instancja `http` (base URL = SSOT env), a błędy normalizuje response-interceptor przez czystą `toApiError` (testowalną bez sieci):

```ts
import axios, { AxiosError } from 'axios';
import type { ValidationErrors } from '../types';

export class ApiError extends Error {
  constructor(readonly status: number, message: string, readonly validationErrors?: ValidationErrors) {
    super(message);
  }
}

interface ProblemDetails { title?: string; errors?: ValidationErrors; }

export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  headers: { 'Content-Type': 'application/json' },
});

export function toApiError(error: AxiosError<ProblemDetails>): ApiError {
  const status = error.response?.status ?? 0;
  const problem = error.response?.data;
  return new ApiError(status, problem?.title ?? error.message, problem?.errors);
}

http.interceptors.response.use((response) => response, (error) => Promise.reject(toApiError(error)));
```

Endpointy promptów — cienki wrapper nad `http`; pq-5 dokłada tu `getPrompts` (`http.get<PromptResponse[]>`), nic więcej:

```ts
import { http } from './client/client';
import type { CreatePromptsRequest, CreatePromptsResponse } from './types';

export async function createPrompts(prompts: string[], signal?: AbortSignal) {
  const { data } = await http.post<CreatePromptsResponse>(
    '/api/v1/prompts', { prompts } satisfies CreatePromptsRequest, { signal });
  return data;
}
```

Hook wysyłki — SSOT cyklu żądania (`useReducer`); `submit` **zwraca `boolean`** (sukces), by formularz nie kasował treści po nieudanym POST:

```ts
import { useReducer } from 'react';
import { createPrompts } from '../api/prompts';
import { ApiError } from '../api/client';
import type { ValidationErrors } from '../api/types';

type SubmitState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'success'; count: number }
  | { status: 'error'; message: string; validationErrors?: ValidationErrors };

export function useCreatePrompts() {
  const [state, dispatch] = useReducer(reducer, { status: 'idle' });

  const submit = async (prompts: string[]): Promise<boolean> => {
    dispatch({ type: 'submit' });
    try {
      await createPrompts(prompts);
      dispatch({ type: 'success', count: prompts.length });
      return true;
    } catch (error) {
      const message = error instanceof ApiError ? error.message : 'Nie udało się wysłać promptów.';
      const validationErrors = error instanceof ApiError ? error.validationErrors : undefined;
      dispatch({ type: 'error', message, validationErrors });
      return false;
    }
  };

  return { state, submit };
}
```

Stan pól formularza — pure reducer (testowalny bez renderu) + walidacja pustych:

```ts
import type { PromptField } from '../features/prompts/types';

export const isBlank = (value: string): boolean => value.trim().length === 0;

export type PromptFieldsAction =
  | { type: 'add' }
  | { type: 'remove'; id: string }
  | { type: 'change'; id: string; value: string }
  | { type: 'reset' };

export function promptFieldsReducer(fields: PromptField[], action: PromptFieldsAction): PromptField[] { /* ... */ }
```

Komponent domenowy — render-focused; reset **tylko po sukcesie**, blokada double-submit przez `disabled` przy `submitting`. Wysyła treść surową (backend trymuje — § Przepływ danych):

```tsx
export function PromptForm() {
  const { fields, add, remove, change, reset } = usePromptFields();
  const { state, submit } = useCreatePrompts();
  const [submitAttempted, setSubmitAttempted] = useState(false);
  const isSubmitting = state.status === 'submitting';

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitAttempted(true);
    if (fields.some((field) => isBlank(field.value))) return;
    const ok = await submit(fields.map((field) => field.value));
    if (ok) {
      reset();
      setSubmitAttempted(false);
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      {fields.map((field, index) => (
        <PromptField
          key={field.id}
          index={index}
          value={field.value}
          invalid={submitAttempted && isBlank(field.value)}
          disabled={isSubmitting}
          onChange={(value) => change(field.id, value)}
          onRemove={() => remove(field.id)}
        />
      ))}
      <Button type="button" disabled={isSubmitting} onClick={add}>Dodaj prompt</Button>
      <Button type="submit" disabled={isSubmitting}>Wyślij</Button>
      {/* <Alert> z state (success/error) */}
    </form>
  );
}
```

Pole promptu — a11y (etykieta „Prompt N" przez `aria-label`), `disabled` w trakcie wysyłki:

```tsx
interface PromptFieldProps {
  index: number;
  value: string;
  invalid: boolean;
  disabled: boolean;
  onChange: (value: string) => void;
  onRemove: () => void;
}

export function PromptField({ index, value, invalid, disabled, onChange, onRemove }: PromptFieldProps) {
  return (
    <div>
      <TextArea
        aria-label={`Prompt ${index + 1}`}
        placeholder="Wpisz prompt…"
        value={value}
        invalid={invalid}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
      <Button type="button" variant="ghost" disabled={disabled} onClick={onRemove}>Usuń</Button>
    </div>
  );
}
```

Chat-input taniej pure-CSS (bez JS), `TextArea.module.css`: `field-sizing: content` (auto-rośnie z treścią, Chromium 123+) + `min-height`/`max-height` jako granice i fallback tam, gdzie `field-sizing` nieobsługiwane, + `resize: vertical` jako ręczna furtka.

Delta backendu (`Program.cs`) — dev-only CORS obok dev-proxy (§ Kluczowe decyzje). Middleware CORS jest bezczynny bez nagłówka `Origin`, więc testy integracyjne (bez `Origin`) niezmienione:

```csharp
builder.Services.AddCors();
// ...
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
```

## Kontrakt API (fragment konsumowany)

Weryfikacja u źródła: [pq-2 → L261](pq-2.md#L261). JSON camelCase; enum jako string camelCase.

| Wywołanie | Żądanie | Sukces | Błąd |
|---|---|---|---|
| `POST /api/v1/prompts` | `{ "prompts": ["...","..."] }` | `200` `{ "ids": ["<guid>",…], "status": "pending" }` | `400` `application/problem+json`, `errors` z kluczami `prompts` / `prompts[i]`; `500` ProblemDetails |

Limity backendu (SSOT — **nie duplikować** liczb): 50 promptów/żądanie, 8000 znaków/prompt po trymie, puste/whitespace odrzucane. Front waliduje jedynie pustość (UX). `GET /api/v1/prompts` (`PromptResponse[]`) — konsumowany dopiero w pq-5, typ już zadeklarowany.

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---|---|---|
| Scaffold Vite (index.html, main.tsx, tsconfig*, package.json) | setup | `frontend/` |
| `vite.config.ts` — `defineConfig` z **`vitest/config`**: react + `server.proxy` + `test` | setup | `frontend/vite.config.ts` |
| `.env.example` (`VITE_API_BASE_URL=`) + typowanie env | setup | `frontend/.env.example`, `frontend/src/vite-env.d.ts` |
| `setupTests.ts` (jest-dom) | test | `frontend/src/setupTests.ts` |
| `App.tsx` (composition root: nagłówek + `PromptForm`) | app | `frontend/src/App.tsx` |
| Typy DTO + `PromptStatus` + `ValidationErrors` | api | `frontend/src/api/types.ts` |
| `apiFetch` + `ApiError` | api | `frontend/src/api/client.ts` |
| `createPrompts` | api | `frontend/src/api/prompts.ts` |
| `useCreatePrompts` (reducer, `submit → boolean`) | hooks | `frontend/src/hooks/useCreatePrompts.ts` |
| `PromptField` (typ) | feature | `frontend/src/features/prompts/types.ts` |
| `promptFieldsReducer` + `usePromptFields` + `isBlank` | feature | `frontend/src/features/prompts/usePromptFields.ts` |
| `PromptForm` (reset po sukcesie, blokada double-submit) (+ `.module.css`) | feature | `frontend/src/features/prompts/PromptForm.tsx` |
| `PromptField` (a11y `aria-label`, `disabled`) (+ `.module.css`) | feature | `frontend/src/features/prompts/PromptField.tsx` |
| `Button` (+ `.module.css`) | ui | `frontend/src/components/ui/Button.tsx` |
| `TextArea` (chat-input) (+ `.module.css`) | ui | `frontend/src/components/ui/TextArea.tsx` |
| `Alert` (+ `.module.css`) | ui | `frontend/src/components/ui/Alert.tsx` |
| Testy logiki (3 pliki, § Strategia testowania) | test | `frontend/src/**/*.test.ts` |
| **MOD** `AddCors()` + dev-only `UseCors` | backend Api | `backend/src/PromptQueue.Api/Program.cs` |

Uwaga: task mówi „reużywalne `Input`/`Button`" — tu `Input` zastąpiony `TextArea`, bo prompt jest wielolinijkowy; wzorzec generalizuje się na `Input`, gdy pojawi się pole jednoliniowe (YAGNI — nie teraz).

## Przepływ danych

1. Edycja/`Dodaj`/`Usuń` → dispatch w `usePromptFields` (`crypto.randomUUID()` jako stabilny `key`).
2. Submit → `setSubmitAttempted(true)`; jeśli którekolwiek pole `isBlank` → przerwij (puste podświetlone). Inaczej `ok = await submit(fields.map(f => f.value))`.
3. `submit` → `createPrompts` → `apiFetch` POST. **Front wysyła treść surową**; trymowanie i limity to SSOT backendu ([pq-2 → L310](pq-2.md#L310)) — front sprawdza tylko pustość (`value.trim()`).
4. `ok === true` (`200`) → `Alert` sukcesu + `reset()` + `setSubmitAttempted(false)`. `ok === false` → treść **zostaje**, `Alert` błędu z `ApiError.validationErrors` (`400`) lub komunikatem fallback (sieć/`500`).
5. W trakcie `submitting` przyciski Wyślij/Dodaj/Usuń i pola `disabled` — brak double-submit i mutacji w locie.

Bazowy URL czytany raz w `client.ts`. Dev: `VITE_API_BASE_URL` puste → ścieżki względne `/api/...` → Vite dev-proxy na backend; dev-CORS w Api jako druga warstwa wygody (§ Kluczowe decyzje).

## Zarządzanie stanem (SSOT)

- **Wartości pól** → `usePromptFields` (`useReducer`, stan ulotny).
- **Cykl wysyłki** → `useCreatePrompts` (`useReducer`).
- **Błąd pustego pola** → pochodna `submitAttempted && isBlank(value)`, bez osobnego stanu.
- **Limity promptów** → backend (front nie kopiuje liczb).
- **Base URL API** → `VITE_API_BASE_URL`, jeden odczyt w `client.ts`.
- **Słownik statusów** → `PromptStatus` w `api/types.ts` (lustro enuma backendu; `StatusBadge` pq-5 konsumuje).

## Kluczowe decyzje

- **Stylowanie: CSS Modules.** Decyzja użytkownika (2026-07-15, priorytet: szybkość). Zero zależności runtime, wbudowane w Vite, klasy scoped w `*.module.css` obok komponentu — realizuje regułę code-frontend „style oddzielone od renderu". Tailwind (utility w JSX) i MUI (ciężka zależność) odrzucone.
- **Wpisywanie: dynamiczne pola add/remove, każde `TextArea` w stylu chat-input.** Decyzja użytkownika (2026-07-15). „Jedno textarea, prompt na linię" wyklucza prompty wieloliniowe (nowe linie znaczące — [pq-2 → L310](pq-2.md#L310)) → odrzucone. Wygląd chat-input osiągnięty pure-CSS: `field-sizing: content` + `min-height`/`max-height` + `resize: vertical` — bez JS.
- **Łączność: dev-proxy Vite ORAZ dev-only CORS w Api.** Decyzja użytkownika (2026-07-15, „razem z CORS-em"). Proxy `server.proxy['/api'] → http://localhost:5269` (zweryfikowany port profilu http backendu) dla wygody dev; dodatkowo `AddCors()` + `UseCors(AllowAny…)` **tylko w `IsDevelopment()`** — obsługuje bezpośrednie wołania z innego originu w dev. To **jedyna zmiana backendu w pq-4**; middleware CORS bezczynny bez nagłówka `Origin`, więc testy integracyjne (bez `Origin`; test `500` w env `Production`) niezmienione. Prod-CORS rozstrzyga pq-6, jeśli front trafi na inny origin niż Api.
- **Reset formularza tylko po sukcesie POST.** `submit` zwraca `boolean`; `reset()` warunkowany `ok` — nieudany POST nie kasuje wpisanych promptów.
- **Blokada double-submit.** Przyciski Wyślij/Dodaj/Usuń i pola `disabled` przy `state.status === 'submitting'` — brak duplikatów promptów.
- **Testy frontu: lekkie, 3 pliki czystej logiki** (prostsze niż backend). Decyzja użytkownika (2026-07-15). Vitest + RTL jako fundament dla hooka pollingu pq-5; bez rozbudowy.
- **Błędy `400` zbiorczo w `Alert`.** Decyzja użytkownika (2026-07-15). Mapowanie `prompts[i]` → konkretne pole = YAGNI (odłożone).
- **strict TS = szablon Vite `react-ts`** (`strict: true`); `vite.config.ts` przez `defineConfig` z `vitest/config` (poprawne typowanie bloku `test`); `any` zakazane.
- **Reducery używają `switch`** — idiomatyczne dla `useReducer`; reguła „słownik zamiast switcha" dotyczy mapowania wartość→wynik (np. `StatusBadge` pq-5), nie reducerów.
- **Klient HTTP: axios** (decyzja użytkownika 2026-07-15, wbrew pierwotnemu `fetch`). Instancja `http` + response-interceptor mapujący błędy na `ApiError` (czysta `toApiError`, testowana bez sieci); pq-5 dokłada `getPrompts` przez `http.get`. Ubocznie znika handoff „bodyless GET / `response.json()` na pustym body" (axios sam deserializuje). Zależność runtime świadomie dodana; `apiFetch`/`fetch` z pierwotnego v2 wycofane.
- **Wszystkie custom hooki w `hooks/`, folder-per-moduł.** Decyzja użytkownika 2026-07-15 (koryguje pierwotny podział „data-hook w `hooks/`, form-hook przy feature" — powodował mismatch nazwa↔lokalizacja). `useCreatePrompts`, `usePromptFields` (+ pq-5 `usePrompts`/`usePromptPolling`) → własny folder pod `hooks/` (plik + `.test.ts`); analogicznie `api/client/`. Skill `code-frontend` reguła 9.
- **Poprawki z CR (naniesione w implementacji):** (W1) `promptFieldsReducer.remove` nie usuwa ostatniego pola + „Usuń" `disabled` przy jednym polu → niemożliwy pusty POST/400; (W2) `validationErrors` z 400 renderowane w `Alert` (nie tylko generyczny `title`). Szczegóły: [raport pq-4](../implementation-reports/pq-4.md).
- **HANDOFF pq-6:** `crypto.randomUUID()` wymaga secure context (localhost/https); przy serwowaniu po http na LAN klucze pól się nie wygenerują — w pq-6 udokumentować https/localhost lub przewidzieć fallback id.
- **HANDOFF pq-5:** przy dokładaniu `getPrompts` do `apiFetch` uwzględnić bodyless GET — `Content-Type` nieszkodliwy, ale `response.json()` na pustym body rzuci; drobna korekta klienta po stronie pq-5, **nie** przebudowywać teraz.

## Plan implementacji

1. Scaffold: `npm create vite@latest frontend -- --template react-ts`; oczyść boilerplate (`App.css`, logo) — `frontend/`.
2. `vite.config.ts`: `import { defineConfig } from 'vitest/config'`; `server.proxy['/api'] = { target: 'http://localhost:5269', changeOrigin: true }`; blok `test` (`environment: 'jsdom'`, `setupFiles: './src/setupTests.ts'`).
3. `.env.example` (`VITE_API_BASE_URL=`) + typowanie w `frontend/src/vite-env.d.ts`.
4. Warstwa `api/`: `types.ts`, `client.ts` (`apiFetch` + `ApiError`), `prompts.ts` (`createPrompts`) — `frontend/src/api/`.
5. `features/prompts/types.ts` (`PromptField`) + `usePromptFields.ts` (pure `promptFieldsReducer` + `isBlank`).
6. `hooks/useCreatePrompts.ts` (reducer; `submit → Promise<boolean>`).
7. `components/ui/`: `Button`, `TextArea` (chat-input: `field-sizing`/`min-height`/`resize`), `Alert` (+ `.module.css`).
8. `features/prompts/PromptField.tsx` (a11y `aria-label`, `disabled`) i `PromptForm.tsx` (reset po sukcesie, `disabled` przy `submitting`, walidacja pustych) (+ `.module.css`).
9. `App.tsx` montuje `PromptForm`.
10. Backend MOD: `AddCors()` + dev-only `UseCors(AllowAny…)` — `backend/src/PromptQueue.Api/Program.cs`.
11. `setupTests.ts` (jest-dom) + 3 pliki testów (§ Strategia testowania).
12. Weryfikacja: `dotnet run --project backend/src/PromptQueue.Api` + `npm run dev` → dodanie 2–3 promptów → `200` i feedback; nieudany POST nie kasuje treści; puste pole blokuje; podwójny submit niemożliwy; `npm run test` i `npm run build` zielone.

## Strategia testowania

Vitest + React Testing Library — lekko, prościej niż backend. „Testuj logikę, nie framework": pomijamy render bez logiki, dev-proxy, wewnętrzne React. Backend niezmieniony (dev-only CORS bezczynny bez `Origin`).

- **`api/client.ts` (unit, mock `fetch`)** — `client.test.ts`: `200` → sparsowane body; `400` `application/problem+json` → `ApiError` z `validationErrors` (klucze `prompts`/`prompts[0]`); `500` → `ApiError` ze statusem.
- **`promptFieldsReducer` (unit, bez renderu)** — `usePromptFields.test.ts`: `add` dopisuje puste pole; `remove` po `id`; `change` aktualizuje wskazane pole; `reset` → jedno puste; `isBlank` na `''`/`'   '`/`'x'`.
- **`useCreatePrompts` (RTL `renderHook`, mock `api/prompts`)** — `useCreatePrompts.test.ts`: sukces → `status='success'`, `count`, `submit` zwraca `true`; `ApiError` → `status='error'` + `message`/`validationErrors`, `submit` zwraca `false`.
- **Pomijamy**: `Button`/`TextArea`/`Alert`/`PromptField` (prezentacja), routing/proxy, serializację, testy backendu (bez zmian logicznych).

## Pytania do użytkownika

Brak otwartych pytań — wszystkie kwestie (stylowanie, sposób wpisywania, łączność dev-proxy + dev-CORS, zakres testów, obsługa błędów `400`) rozstrzygnięte decyzjami użytkownika z 2026-07-15 (patrz § Kluczowe decyzje).

## Krytyka (solution-critic)

**v1 — Werdykt: AKCEPTUJ Z UWAGAMI** (bez blokerów). Zweryfikowane: typy TS = wierne lustro kontraktu pq-2 (camelCase, `errors`, `title`); `apiFetch`/`toApiError` poprawne dla 400/500; port dev-proxy `5269` potwierdzony w `launchSettings.json`; fakty (szablon `react-ts` strict, `satisfies`, `renderHook`, Vitest) aktualne; pełna zgodność ze skillem `code-frontend`.

Uwagi v1 (wszystkie naniesione w v2):

| # | Waga | Uwaga | Stan w v2 |
|---|------|-------|-----------|
| 1 | ISTOTNE | Reset formularza bezwarunkowo po `await submit` → nieudany POST kasował wpisane prompty | ✅ `submit → Promise<boolean>`, `reset()` tylko przy `ok` |
| 2 | ISTOTNE | Brak ochrony przed double-submit (duplikaty promptów) | ✅ `disabled` przy `submitting` (Wyślij/Dodaj/Usuń/pola) |
| 3 | DROBNE | Blok `test` wymaga `defineConfig` z `vitest/config` pod strict TS | ✅ plan krok 2 + struktura |
| 4 | DROBNE | Brak etykiety a11y dla `TextArea` | ✅ `aria-label="Prompt N"` w `PromptField` |
| 5 | DROBNE | `crypto.randomUUID()` wymaga secure context | ✅ HANDOFF pq-6 w Kluczowych decyzjach |
| 6 | obs. | `apiFetch` przy bodyless GET (pq-5): `response.json()` na pustym body rzuci | ✅ HANDOFF pq-5 |

**v2 — delta-krytyka: AKCEPTUJ** (zero blokerów/istotnych). Zweryfikowane: overload `app.UseCors(Action<CorsPolicyBuilder>)` istnieje, kolejność względem `UseExceptionHandler` zgodna z rekomendacją MS, `AllowAnyOrigin` bramkowane `IsDevelopment()` bezpieczne, testy pq-2 faktycznie nietknięte (CORS bezczynny bez `Origin`; test 500 w env Production poza polityką); `field-sizing: content` = realna właściwość CSS (Chromium 123+), fallback min/max-height poprawna progresywna degradacja; flow submit domknięty (blokada double-submit przez `disabled` z reducera wystarczająca — dispatch synchroniczny przed await, React commit'uje między zdarzeniami UI). 2 obserwacje bez akcji: dev-CORS częściowo redundantny z proxy (świadome „pas + szelki"), nagłówki CORS na 500 z exception handlera (bez znaczenia w dev).

## Historia wersji

- v1 (2026-07-15): projekt system-architect (fundament frontu: api/hooks/features/ui, CSS Modules, dynamiczne pola TextArea, dev-proxy zamiast CORS, Vitest+RTL) + krytyka (AKCEPTUJ Z UWAGAMI: 2 istotne, 4 drobne).
- v2 (2026-07-15): decyzje użytkownika — CSS Modules (szybkość), dynamiczne pola z UX chat-input (`field-sizing: content`), **dev-proxy + dev-only CORS w Api** (jedyna zmiana backendu), testy lekkie potwierdzone, błędy 400 zbiorczo; naniesione uwagi krytyka (reset tylko po sukcesie, blokada double-submit, `vitest/config`, `aria-label`, handoffy pq-5/pq-6).
