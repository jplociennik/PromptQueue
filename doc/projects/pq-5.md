# Projekt: pq-5 — Frontend: lista promptów + polling statusów

> Data: 2026-07-15
> Wersja: 2
> Werdykt: AKCEPTUJ (wariant always-poll; krytyka AKCEPTUJ Z UWAGAMI — uwagi naniesione)

## Cel

Dokleić do fundamentu pq-4 drugą ścieżkę użytkownika: **żywą listę wszystkich promptów** ze statusem, wynikiem (`completed`) lub błędem (`failed`), odświeżaną automatycznie przez polling `GET /api/v1/prompts`, ze sprzątaniem przy odmontowaniu (kluczowy wymóg DoD). Źródło: [pq-5 → L13](../prepare-projects/pq-5.md#L13), [DoD → L7](../prepare-projects/DoD.md#L7). Zależy od kontraktu [pq-2 GET → L262](pq-2.md#L262) i fundamentu [pq-4](pq-4.md) (warstwa `api/`, typy, axios `http`). Zakres: `getPrompts`, hook `usePromptPolling`, `StatusBadge`, `PromptList`/`PromptRow`, wpięcie w `App.tsx`, jedna additywna zmiana `PromptForm` (`onSubmitted`). **Bez przeróbek fundamentu** — pq-4 dostarczył wszystkie szwy. Poza zakresem: websockety, paginacja, filtrowanie, timestampy w wierszu (YAGNI).

## Proponowana architektura

Zachowana warstwowość pq-4 `components/UI ← features ← (hooks, api)`, jedyna brama HTTP to `api/`. Logika (fetch cykliczny co 2 s, cleanup) w custom hooku `usePromptPolling`; komponenty (`PromptList`/`PromptRow`) czysto renderujące (separacja prezentacja/logika — skill reguła 7). `App.tsx` jako composition root koordynuje dwa feature'y siostrzane: `usePromptPolling` żyje w `App`, `prompts`+stan płyną w dół do `PromptList` (props), a `refetch` w dół do `PromptForm` — to **natychmiast odświeża listę po dodaniu promptów** (bez czekania na kolejny tick; § Kluczowe decyzje D2/D3).

```
frontend/src/
  api/prompts.ts                       # + getPrompts (dopisek do createPrompts)
  hooks/usePromptPolling/              # NEW — pętla pollingu (setTimeout rekursywny + AbortController)
    usePromptPolling.ts  usePromptPolling.test.ts
  components/UI/StatusBadge/           # NEW — reużywalny badge (słownik statusów)
    StatusBadge.tsx  StatusBadge.module.css  StatusBadge.test.tsx
  features/prompts/
    PromptList/  PromptList.tsx  PromptList.module.css   # NEW — prezentacyjny
    PromptRow/   PromptRow.tsx   PromptRow.module.css    # NEW — prezentacyjny
    PromptForm/PromptForm.tsx            # MOD — dodaj prop onSubmitted?
  App.tsx                                # MOD — usePromptPolling + <PromptList/> + onSubmitted
```

### Kluczowe abstrakcje (kod)

**`getPrompts`** — cienki wrapper nad `http` (bliźniak `createPrompts`; axios sam deserializuje, więc handoff pq-4 „bodyless GET / `response.json()`" jest bezprzedmiotowy):

```ts
export async function getPrompts(signal?: AbortSignal) {
  const { data } = await http.get<PromptResponse[]>('/api/v1/prompts', { signal });
  return data;
}
```

**`usePromptPolling`** — SSOT listy i cyklu odświeżania. Rekursywny `setTimeout` (następny GET dopiero po zakończeniu poprzedniego → brak nakładania wolnych żądań); `AbortController` zwalnia żądanie w locie przy teardownie; flaga `cancelled` blokuje `dispatch`/reschedule po odmontowaniu; `trigger` (bump przez `refetch`) restartuje pętlę = pełny teardown + natychmiastowy tick (natychmiastowe odświeżenie po POST). **Always-poll (decyzja użytkownika): reschedule bezwarunkowy** — po każdym sukcesie i błędzie pętla planuje kolejny tick za `POLL_INTERVAL_MS` (brak warunku stopu). Stan przez `useReducer` (spójne przejście `{prompts,status,error}`, jak `useCreatePrompts`), stale-while-revalidate: błąd zachowuje ostatnią dobrą listę; błąd sieci (`status 0`) mapowany na komunikat polski (§ Kluczowe decyzje D7).

```ts
const POLL_INTERVAL_MS = 2000;

type PollingStatus = 'loading' | 'success' | 'error';
interface PollingState { prompts: PromptResponse[]; status: PollingStatus; error: string | null; }
type PollingAction =
  | { type: 'success'; prompts: PromptResponse[] }
  | { type: 'error'; message: string };

const initialState: PollingState = { prompts: [], status: 'loading', error: null };

function pollingReducer(state: PollingState, action: PollingAction): PollingState {
  switch (action.type) {
    case 'success': return { prompts: action.prompts, status: 'success', error: null };
    case 'error':   return { ...state, status: 'error', error: action.message };
  }
}

export function usePromptPolling() {
  const [state, dispatch] = useReducer(pollingReducer, initialState);
  const [trigger, setTrigger] = useState(0);
  const refetch = () => setTrigger((n) => n + 1);

  useEffect(() => {
    let cancelled = false;
    let timerId: ReturnType<typeof setTimeout>;
    const controller = new AbortController();

    const tick = async () => {
      try {
        const prompts = await getPrompts(controller.signal);
        if (cancelled) return;
        dispatch({ type: 'success', prompts });
        timerId = setTimeout(tick, POLL_INTERVAL_MS);
      } catch (error) {
        if (cancelled) return;
        const message = error instanceof ApiError && error.status !== 0 ? error.message : 'Nie udało się pobrać promptów.';
        dispatch({ type: 'error', message });
        timerId = setTimeout(tick, POLL_INTERVAL_MS);
      }
    };

    tick();

    return () => { cancelled = true; controller.abort(); clearTimeout(timerId); };
  }, [trigger]);

  return { prompts: state.prompts, status: state.status, error: state.error, refetch };
}
```

**`StatusBadge`** — reużywalny, mapowanie etykiety słownikiem `Record<PromptStatus, string>` (skill reguła 5; typ wymusza kompletność 4 statusów w compile-time), klasa CSS przez `styles[status]` (idiom `styles[variant]` z `Alert`/`Button`):

```tsx
const STATUS_LABELS: Record<PromptStatus, string> = {
  pending: 'Oczekuje',
  processing: 'Przetwarzanie',
  completed: 'Zakończono',
  failed: 'Błąd',
};

interface StatusBadgeProps { status: PromptStatus; }

export function StatusBadge({ status }: StatusBadgeProps) {
  return <span className={`${styles.badge} ${styles[status]}`}>{STATUS_LABELS[status]}</span>;
}
```

**`PromptList`** (prezentacyjny — props zamiast hooka; § Kluczowe decyzje D3) — gałęzie loading/pusto/lista + stale-while-revalidate `Alert` (reużyty z pq-4); stabilny `key = prompt.id`:

```tsx
interface PromptListProps { prompts: PromptResponse[]; status: PollingStatus; error: string | null; }

export function PromptList({ prompts, status, error }: PromptListProps) {
  if (status === 'loading') return <p className={styles.info}>Ładowanie promptów…</p>;
  return (
    <section className={styles.list}>
      {status === 'error' && <Alert variant="error">{error}</Alert>}
      {prompts.length === 0
        ? <p className={styles.info}>Brak promptów.</p>
        : <ul className={styles.items}>{prompts.map((p) => <PromptRow key={p.id} prompt={p} />)}</ul>}
    </section>
  );
}
```

**`PromptRow`** — treść zawsze; `result` w `<pre>` dla `completed` (zachowuje formatowanie odpowiedzi LLM), `errorMessage` dla `failed` (kontrakt gwarantuje niepustość — guard defensywny i domyka typ `string | null`):

```tsx
interface PromptRowProps { prompt: PromptResponse; }

export function PromptRow({ prompt }: PromptRowProps) {
  return (
    <li className={styles.row}>
      <StatusBadge status={prompt.status} />
      <p className={styles.content}>{prompt.content}</p>
      {prompt.status === 'completed' && prompt.result && <pre className={styles.result}>{prompt.result}</pre>}
      {prompt.status === 'failed' && prompt.errorMessage && <p className={styles.error}>{prompt.errorMessage}</p>}
    </li>
  );
}
```

**`App.tsx`** (composition root) i delta `PromptForm` (additywny, opcjonalny prop — pq-4 niezłamany):

```tsx
export default function App() {
  const { prompts, status, error, refetch } = usePromptPolling();
  return (
    <main>
      <h1>PromptQueue</h1>
      <PromptForm onSubmitted={refetch} />
      <PromptList prompts={prompts} status={status} error={error} />
    </main>
  );
}
```

```tsx
interface PromptFormProps { onSubmitted?: () => void; }
// w handleSubmit, po if (ok) { reset(); setSubmitAttempted(false); onSubmitted?.(); }
```

## Kontrakt API (fragment konsumowany)

Weryfikacja u źródła: [pq-2 → L262](pq-2.md#L262). JSON camelCase; `status` string camelCase (mapuje się wprost na `StatusBadge`).

| Wywołanie | Sukces | Błąd |
|---|---|---|
| `GET /api/v1/prompts` | `200` `[ { id, content, status, result, errorMessage, createdAt, updatedAt }, … ]`, kolejność `CreatedAt`+`Id` **gwarantowana przez repo** (front nie sortuje) | `500` `application/problem+json` → `ApiError` (interceptor pq-4) |

`result` niepuste dla `completed`, `errorMessage` niepuste dla `failed` — front rozróżnia po `status` ([pq-2 → L265](pq-2.md#L265)).

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---|---|---|
| `getPrompts` (dopisek) | api | `frontend/src/api/prompts.ts` |
| `usePromptPolling` (pętla always-poll + reducer + `refetch`) | hooks | `frontend/src/hooks/usePromptPolling/usePromptPolling.ts` |
| `StatusBadge` (słownik etykiet) (+ `.module.css`) | ui | `frontend/src/components/UI/StatusBadge/StatusBadge.tsx` |
| `PromptList` (prezentacyjny) (+ `.module.css`) | feature | `frontend/src/features/prompts/PromptList/PromptList.tsx` |
| `PromptRow` (prezentacyjny) (+ `.module.css`) | feature | `frontend/src/features/prompts/PromptRow/PromptRow.tsx` |
| **MOD** `PromptForm` — prop `onSubmitted?` | feature | `frontend/src/features/prompts/PromptForm/PromptForm.tsx` |
| **MOD** `App` — hook + `<PromptList/>` + `onSubmitted={refetch}` | app | `frontend/src/App.tsx` |
| Test `usePromptPolling` (fake timers) | test | `frontend/src/hooks/usePromptPolling/usePromptPolling.test.ts` |
| Test `StatusBadge` (`it.each` etykiety) + `afterEach(cleanup)` | test | `frontend/src/components/UI/StatusBadge/StatusBadge.test.tsx` |

`PollingStatus` jest współdzielony przez hook i `PromptList` — eksportuj go z `usePromptPolling.ts` (miejsce definicji), importuj w `PromptList` (skill reguła 8, bez duplikacji typu).

## Przepływ danych

SSOT stanu promptu to encja w bazie (pq-1); front tylko odzwierciedla. SSOT listy w kliencie i cyklu odświeżania to `usePromptPolling` (żyje w `App`). Kolejność listy = repo (front nie sortuje — [pq-2 → L300](pq-2.md#L300)).

Flow pętli i odświeżania (czego kod sam nie pokazuje — sekwencja i koordynacja z formularzem):

```
mount App → usePromptPolling: tick() → GET
  sukces → setTimeout(tick, 2s) → … (cykl bez końca — always-poll)
  błąd → dispatch error (lista zostaje) → setTimeout(tick, 2s) retry

PromptForm.submit OK → onSubmitted() → refetch() → trigger++
  → cleanup starej pętli (cancelled=true, abort in-flight, clearTimeout)
  → nowy efekt: tick() natychmiast → nowy pending widoczny bez czekania na tick

unmount → cleanup: cancelled=true, controller.abort(), clearTimeout  ← wymóg DoD
```

Rola dwóch mechanizmów teardownu: `cancelled` blokuje `dispatch`/reschedule (spójność stanu), `controller.abort()` zwalnia żądanie HTTP w locie (zasób sieci). Ponieważ `abort()` odrzuca promise w mikrozadaniu **po** synchronicznym cleanupie (który już ustawił `cancelled=true`), `catch` widzi `cancelled===true` → `return` przed `dispatch` — dlatego **nie** trzeba osobnego `axios.isCancel`. StrictMode (`main.tsx`) podwaja mount/unmount w dev → dodatkowy cykl tick+abort; niegroźny (jedna żywa pętla po ustabilizowaniu) i potwierdza poprawność cleanupu (zweryfikowane przez krytyka).

## Zarządzanie stanem (SSOT)

- **Lista promptów + status/error odświeżania** → `usePromptPolling` (`useReducer`), instancja w `App`.
- **Wyzwalacz natychmiastowego odświeżenia** → `trigger` (`useState`) w `usePromptPolling`; `refetch` go bumpuje.
- **Interwał pollingu** → `POLL_INTERVAL_MS` (jeden stały punkt).
- **Słownik etykiet statusów** → `STATUS_LABELS` w `StatusBadge`; wartości `PromptStatus` = SSOT w `api/types.ts` (pq-4).
- **Kolejność listy** → repo backendu (front nie kopiuje sortu).

## Kluczowe decyzje

- **D1 — Interwał 2000 ms.** Decyzja użytkownika (2026-07-15). Zadania LLM trwają sekundy–minuty, 2 s daje responsywny postęp bez zalewania API. Stała `POLL_INTERVAL_MS`.
- **D2 — Always-poll: pętla odpytuje bez końca (bez stop-when-idle).** Decyzja użytkownika (2026-07-15, świadomie wbrew pierwotnej rekomendacji architekta). Reschedule **bezwarunkowy** po każdym ticku — usunięty warunek `isActive` (oraz jego import `PromptStatus` w hooku, inaczej `noUnusedLocals: true` → `tsc -b` fail). Prostsze niż stop+wznawianie; koszt (1 GET/2 s przy otwartej stronie) pomijalny przy skali demo.
- **D3 — Natychmiastowy refetch po POST; `usePromptPolling` w `App`, `PromptList` prezentacyjny.** Decyzja użytkownika (2026-07-15). Mimo always-poll `refetch`/`onSubmitted` **zachowane celowo** — nowy prompt pojawia się natychmiast, bez czekania ≤2 s na tick. Stąd hook żyje w `App` (by `refetch` trafił do `PromptForm`), a `PromptList` bierze `prompts`/stan przez props (czysta separacja prezentacja/logika — skill reguła 7). `refetch` bumpuje `trigger` → React robi teardown starej pętli (abort in-flight + clearTimeout) przed nowym `tick()` → brak dwóch równoległych pętli (zweryfikowane przez krytyka).
- **D4 — Rekursywny `setTimeout` + `trigger`-restart zamiast `setInterval`.** `setInterval` nakłada wolne żądania; rekursywny `setTimeout` czeka na zakończenie poprzedniego GET, a `refetch` = bump `trigger` w deps efektu reużywa cykl życia efektu Reacta do teardownu (bez ręcznego żonglowania timerem/refami poza efektem).
- **D5 — `StatusBadge`: słownik tylko na etykiety, klasa przez `styles[status]`.** Spójne z `Alert`/`Button` (`styles[variant]`); jeden słownik, `Record<PromptStatus,…>` wymusza kompletność. „Klasa CSS" z mapowania [pq-5 → L19](../prepare-projects/pq-5.md#L19) realizowana idiomem CSS-Modules, nie drugim polem.
- **D6 — Stale-while-revalidate.** Błąd pojedynczego pollingu nie czyści listy (`error` zachowuje `prompts`) i nie zatrzymuje cyklu (retry co interwał); `Alert` błędu nad listą. `loading` tylko dla pierwszego pobrania (brak migotania spinnera co 2 s).
- **D7 — Komunikat błędu sieci po polsku, bez backoffu.** Interceptor axios zawsze zwraca `ApiError`, więc dla błędu sieci (`status 0`, axiosowe „Network Error") hook mapuje na polski fallback „Nie udało się pobrać promptów." (`error.status !== 0 ? error.message : fallback`); błędy serwera (np. 500) pokazują `title` z ProblemDetails. Retry stałe co 2 s bez backoffu — akceptowalne przy skali demo (naniesione z DROBNE #3/#4 krytyki).

## Plan implementacji

Kroki addytywne; kolejność = zależności (api → hook → UI → wpięcie → testy).

1. Dopisz `getPrompts` (`http.get<PromptResponse[]>`) — `frontend/src/api/prompts.ts`.
2. `usePromptPolling` (reducer `{prompts,status,error}`, pętla rekursywna z **bezwarunkowym** reschedule, `refetch`/`trigger`, eksport `PollingStatus`, mapowanie błędu sieci na polski komunikat) — `frontend/src/hooks/usePromptPolling/usePromptPolling.ts`.
3. `StatusBadge` + `.module.css` (klasy `.badge`,`.pending`,`.processing`,`.completed`,`.failed`; paleta z `index.css`: slate/blue/green/red) — `frontend/src/components/UI/StatusBadge/`.
4. `PromptRow` + `.module.css` (`<pre>` dla `result`) — `frontend/src/features/prompts/PromptRow/`.
5. `PromptList` + `.module.css` (import `PollingStatus`, reużyj `Alert`) — `frontend/src/features/prompts/PromptList/`.
6. MOD `PromptForm`: prop `onSubmitted?`, wywołanie w bloku `if (ok)` — `frontend/src/features/prompts/PromptForm/PromptForm.tsx`.
7. MOD `App`: `usePromptPolling` + `<PromptList/>` + `onSubmitted={refetch}` — `frontend/src/App.tsx`.
8. Testy (§ Strategia testowania) — `usePromptPolling.test.ts`, `StatusBadge.test.tsx`.
9. Weryfikacja: `docker compose up -d postgres` + `dotnet run --project backend/src/PromptQueue.Api` + worker (pq-3) + `npm run dev` → dodaj prompty → pojawiają się natychmiast i przechodzą `pending → … → completed/failed` bez reloadu; `npm run test` i `npm run build` zielone.

## Strategia testowania

Vitest + RTL, „testuj logikę, nie framework". `globals: false` → importuj `{ describe, it, expect, vi, beforeEach, afterEach }` z `'vitest'` (jak w istniejących testach); **RTL auto-cleanup nieaktywne → `afterEach(cleanup)`** w każdym pliku renderującym (handoff pq-4).

- **`usePromptPolling` (rdzeń logiki; `renderHook` + `vi.useFakeTimers()`, `vi.mock('../../api/prompts')`, `afterEach(() => { cleanup(); vi.useRealTimers(); })`)** — przypadki: (a) pierwszy tick → `status='success'`, `prompts=dane`; (b) reschedule → po `advanceTimersByTime(2000)` kolejne wywołanie `getPrompts`; (c) **always-poll** — mimo wszystkich `completed` po `advanceTimersByTime(2000)` następuje kolejne wywołanie `getPrompts` (pętla nie stopuje); (d) **cleanup**: `unmount()` → po advance brak wywołań + `signal.aborted === true` (przechwyć argument mocka); (e) `refetch()` → natychmiastowe wywołanie i restart; (f) błąd → `status='error'`, `prompts` zachowane, po advance retry. Między advance flush mikrozadań (`await act(async()=>{})`).
- **`StatusBadge` (mapowanie; `render` + `it.each` 4 statusy → obecna etykieta z `STATUS_LABELS`)** — token-test domykający słownik; plik wprowadza `afterEach(cleanup)`.
- **Pomijamy**: `getPrompts` (trywialny wrapper nad axios — jak `createPrompts` w pq-4; ścieżka weryfikowana przez mock w teście hooka i krok 9), `PromptList`/`PromptRow` (prezentacja bez logiki — spójnie z pq-4), `App` (wiązanie).

## Pytania do użytkownika

Brak otwartych pytań — interwał (2 s), always-poll i natychmiastowy refetch po POST rozstrzygnięte decyzjami użytkownika z 2026-07-15 (patrz § Kluczowe decyzje D1–D3).

## Krytyka (solution-critic)

**Werdykt: AKCEPTUJ Z UWAGAMI** (oceniony wariant always-poll). Rdzeń `usePromptPolling` potwierdzony jako poprawny i **StrictMode-safe** (rekursywny `setTimeout` + `AbortController` + `cancelled`: jedna żywa pętla, cleanup abortuje in-flight, brak stray-dispatch); natychmiastowy refetch po POST bez wyścigu (in-flight abortowany, jedna pętla — nie dwie); `error instanceof ApiError` działa (interceptor axios); `StatusBadge`/typy/testy zgodne ze skillem. Zero blokerów.

Uwagi (naniesione w v2):

| # | Waga | Uwaga | Stan |
|---|------|-------|------|
| 1 | ISTOTNE | Dokument kodował stop-when-idle; wyrównać do always-poll — **usunąć `isActive` + import `PromptStatus`** (inaczej `noUnusedLocals` → `tsc -b` fail), bezwarunkowy reschedule, flip D2/D3, diagram, §Zarządzanie stanem | ✅ naniesione |
| 2 | ISTOTNE | Test „stop gdy completed" przeczy wariantowi — odwrócić na „pętla dalej odpytuje" | ✅ naniesione (test (c)) |
| 3 | DROBNE | Polski fallback komunikatu nieosiągalny (interceptor zawsze `ApiError`) → przy sieci-down user widział ang. „Network Error" | ✅ naniesione: mapowanie `status 0` → polski (D7) |
| 4 | DROBNE | Brak backoffu (stałe 2 s retry) | ✅ zaakceptowane (D7, skala demo) |

## Historia wersji

- v1 (2026-07-15): projekt system-architect (`getPrompts`, `usePromptPolling`, `StatusBadge`, `PromptList`/`PromptRow`, stale-while-revalidate); rekomendacja stop-when-idle.
- v2 (2026-07-15): decyzje użytkownika — interwał 2 s, **always-poll** (usunięty `isActive`/warunek stopu), **natychmiastowy refetch po POST** (`onSubmitted`, hook w `App`, `PromptList` prezentacyjny); krytyka AKCEPTUJ Z UWAGAMI, uwagi 1–4 naniesione.
