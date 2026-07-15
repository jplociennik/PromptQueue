# Raport implementacji: pq-5 — Frontend: lista promptów + polling statusów

> Projekt: [doc/projects/pq-5.md](../projects/pq-5.md)
> Data: 2026-07-15 (1 iteracja: TDD Red→Green + CR; Green dokończony przez koordynatora po awarii subagenta)
> Commit: — (niezacommitowane; branch `pq/pq-4/main`)
> MR: —
> Code review: 1× code-reviewer (**AKCEPTUJ** — 0 krytycznych, 1 ostrzeżenie naprawione, 2 sugestie kosmetyczne). Build 0 błędów, testy 23/23 zielone.

## Co realizuje task

Druga ścieżka użytkownika na fundamencie pq-4: żywa lista wszystkich promptów ze statusem (`StatusBadge`), wynikiem (`completed`) lub błędem (`failed`), odświeżana automatycznie przez polling `GET /api/v1/prompts` co 2 s (wariant **always-poll**, decyzja użytkownika), z pełnym cleanupem przy odmontowaniu (wymóg DoD). Dodanie promptów (pq-4) natychmiast odświeża listę przez `refetch` (bez czekania na kolejny tick). Bez przeróbek fundamentu — tylko additywne zmiany `App.tsx`/`PromptForm.tsx`/`api/prompts.ts`.

## Stan implementacji vs. projekt (v2)

| Decyzja projektu | Stan |
|------------------|------|
| Interwał pollingu 2000 ms | ✅ `POLL_INTERVAL_MS` w hooku |
| **Always-poll** — bezwarunkowy reschedule (bez stop-when-idle) | ✅ [usePromptPolling.ts](../../frontend/src/hooks/usePromptPolling/usePromptPolling.ts) |
| Rekursywny `setTimeout` + `AbortController` + cleanup na unmount (DoD) | ✅ tamże |
| Natychmiastowy `refetch` po sukcesie POST | ✅ `PromptForm.onSubmitted` → `App` → `refetch` |
| `StatusBadge` — słownik `Record<PromptStatus,string>`, nie switch | ✅ [StatusBadge.tsx](../../frontend/src/components/UI/StatusBadge/StatusBadge.tsx) |
| `PromptList`/`PromptRow` prezentacyjne (props, nie hook) | ✅ folder-per-component |
| Błąd sieci (`status 0`) → polski komunikat fallback | ✅ w hooku |
| Stale-while-revalidate (błąd nie czyści listy) | ✅ |

## Zakres zmian

### Frontend (`frontend/src/`)

| Plik | Op | Opis |
|------|----|------|
| [hooks/usePromptPolling/usePromptPolling.ts](../../frontend/src/hooks/usePromptPolling/usePromptPolling.ts) | NEW | Pętla pollingu always-poll: reducer `{prompts,status,error}`, rekursywny `setTimeout`, `AbortController`+`cancelled`, `trigger`/`refetch` |
| [components/UI/StatusBadge/StatusBadge.tsx](../../frontend/src/components/UI/StatusBadge/StatusBadge.tsx) (+`.module.css`) | NEW | Słownik `STATUS_LABELS`, klasa przez `styles[status]` |
| [features/prompts/PromptList/PromptList.tsx](../../frontend/src/features/prompts/PromptList/PromptList.tsx) (+`.module.css`) | NEW | Prezentacyjny: loading/pusto/lista + `Alert` błędu (stale-while-revalidate) |
| [features/prompts/PromptRow/PromptRow.tsx](../../frontend/src/features/prompts/PromptRow/PromptRow.tsx) (+`.module.css`) | NEW | Treść, `StatusBadge`, `result`/`errorMessage` z guardami |
| [api/prompts.ts](../../frontend/src/api/prompts.ts) | MOD | + `getPrompts` (wrapper `http.get`) |
| [App.tsx](../../frontend/src/App.tsx) | MOD | `usePromptPolling` + `<PromptList/>` + `onSubmitted={refetch}` |
| [features/prompts/PromptForm/PromptForm.tsx](../../frontend/src/features/prompts/PromptForm/PromptForm.tsx) | MOD | Additywny opcjonalny prop `onSubmitted?`, wołany tylko przy `ok===true` |

### Testy

| Plik | Op | Pokrycie |
|------|----|----------|
| [usePromptPolling.test.ts](../../frontend/src/hooks/usePromptPolling/usePromptPolling.test.ts) | NEW | 6: pierwszy tick, reschedule po 2s, always-poll mimo `completed`, cleanup+abort na unmount, `refetch` natychmiastowy, błąd+retry+lista zachowana |
| [StatusBadge.test.tsx](../../frontend/src/components/UI/StatusBadge/StatusBadge.test.tsx) | NEW | 4: `it.each` etykiety dla wszystkich statusów |

## Wyniki testów

| Plik testowy | Testy |
|---------|-------|
| usePromptFields.test.ts | 7 |
| client.test.ts | 3 |
| useCreatePrompts.test.ts | 3 |
| StatusBadge.test.tsx | 4 |
| usePromptPolling.test.ts | 6 |
| **Razem** | **23/23 zielone** |

`npm run build` (tsc + vite): **0 błędów**.

## Werdykt CR

**AKCEPTUJ** — 0 krytycznych, 1 ostrzeżenie (naprawione), 2 sugestie kosmetyczne (nieprzyjęte).

| # | Plik | Opis | Status |
|---|------|------|--------|
| O1 | [PromptList.tsx](../../frontend/src/features/prompts/PromptList/PromptList.tsx) | Błąd pierwszego pobrania (`status='error'`, `prompts=[]`) renderował jednocześnie `Alert` błędu i „Brak promptów." — mylące | ✅ naprawione: „Brak promptów." pominięte gdy `status==='error'` |
| S1 | StatusBadge.module.css vs Alert.module.css | Różne przystanki palety Tailwind (-100/-700 vs -50/-800) | ⏳ nieprzyjęte: rodziny kolorów (slate/blue/green/red) spójne, różnica kosmetyczna |
| S2 | Jednostki promienia (px w Button/Alert vs rem w PromptRow) | Niespójność jednostek | ⏳ nieprzyjęte: wartości wizualnie identyczne (0.5rem = 8px), follow-up porządkowy |

## Wątki do uwagi przy CR

1. **GREEN dokończony przez koordynatora, nie przez subagenta implementatora.** Subagent RED napisał testy i stuby poprawnie; subagent GREEN **padł na organizacyjnym limicie wydatków API** tuż na starcie (stuby nietknięte — potwierdzone przed rozpoczęciem naprawy). Koordynator dokończył logikę hooka, `StatusBadge` i style `PromptList`/`PromptRow` bezpośrednio (bez kolejnego subagenta, by nie ryzykować ponownego trafienia w limit tuż po awarii). Code-reviewer poinformowany o tym kontekście i zweryfikował spójność ze wzorcami z pq-4 (paleta, jednostki, idiom `styles[dynamicKey]`) — bez realnych niespójności.
2. **Always-poll bez limitu backoffu** — świadoma decyzja użytkownika (2026-07-15), retry co 2 s w nieskończoność przy padniętym backendzie. Akceptowalne przy skali demo.

## Follow-up (poza scope)

- **S1/S2** — ewentualne ujednolicenie palety/jednostek CSS przy najbliższym porządkowaniu.
- **Manualna weryfikacja end-to-end** (DoD) — uruchomienie pełnego stosu (Postgres + Api + front) i obserwacja listy aktualizującej się na żywo; planowana osobno.

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | TDD: Red (stuby `usePromptPolling`/`StatusBadge` + testy → 10 red, pq-4 13 zielone) → Green (pętla always-poll, słownik statusów, style list/row — dokończone przez koordynatora po awarii subagenta na limicie wydatków → 23/23) + fix O1 z CR | ✅ AKCEPTUJ |
