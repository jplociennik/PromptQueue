# pq-5 — Frontend: lista promptów + polling statusów

## Opis

**Tytuł:** Lista promptów ze statusami i wynikami, odświeżana na bieżąco

Użytkownik widzi listę wszystkich zgłoszonych promptów z ich aktualnym statusem i wynikiem, a widok odświeża się automatycznie — postęp przetwarzania widać bez przeładowania strony.

Endpoint konsumowany: `GET /api/v1/prompts`

## Projekt wstępny

Cel: widok listy z żywym odświeżaniem stanu. Źródło: [DoD.md L7](DoD.md#L7) (lista wszystkich promptów ze statusami i wynikami, odświeżanie przez prosty polling).

Stack: Vite + React + TS; styl wg skilla `code-frontend`. Zależy od [pq-2](pq-2.md) (GET listy) i [pq-4](pq-4.md) (szkielet frontu, klient API).

Zakres:
- **Widok listy** (`PromptList` / `PromptRow`) — treść promptu, status, wynik (dla `completed`) lub komunikat błędu (dla `failed`).
- **Prezentacja statusu** — reużywalny `StatusBadge` (pending/processing/completed/failed).
- **Polling** — custom hook (np. `usePromptPolling`) odpytujący `GET /api/v1/prompts` w interwale, z poprawnym cleanupem; lista renderowana ze stabilnym `key` (id z backendu).

Definition of Done: lista pokazuje wszystkie prompty; statusy i wyniki aktualizują się samoczynnie w miarę przetwarzania przez workera ([pq-3](pq-3.md)); polling sprząta po sobie przy odmontowaniu.

## Do wyjaśnienia

- [?] Interwał pollingu (np. 2 s)?
- [?] Czy zatrzymywać polling, gdy żaden prompt nie jest już w toku (brak `pending`/`processing`)?
