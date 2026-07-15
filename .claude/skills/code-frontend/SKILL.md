---
name: code-frontend
description: "Wytyczne stylu kodu frontendu (React SPA na Vite, TypeScript): czytelność zapisu, brak duplikacji, słowniki zamiast switchy, hooki trzymane razem, separacja stylów od renderu, wspólne typy w osobnym pliku, minimalizm. Załaduj gdy tworzysz lub modyfikujesz komponenty React, hooki lub moduły frontendu."
user-invocable: false
---

# Styl kodu frontendu (React + Vite + TypeScript)

> Uzupełnia reguły React z agentów `system-architect` / `implementator` (komponenty funkcyjne, hooki, kompozycja/SRP, `useState`/`useReducer`, memoizacja z umiarem). Tam są zasady architektoniczne — **tu konkretne nawyki zapisu kodu**. Źródło: `doc/frontend-styl-kodu.md` (import reguł z Cursora). Stack PromptQueue: React + Vite + **TypeScript** (frontend w całości w TS).

## Zasada nadrzędna

**Prostota i minimalizm** — pisz jak najmniej kodu potrzebnego do rozwiązania. Mniej kodu = mniej do czytania, testowania i utrzymania. Każda reguła niżej służy tej zasadzie.

## Czytelność zapisu

1. **Nie rozbijaj każdego wywołania / destrukturyzacji na osobne linie**, jeśli mieści się czytelnie w jednej. Wieloliniowy zapis rezerwuj dla naprawdę długich list argumentów/propsów.

   Słabo:
   ```tsx
   function PromptForm({
     prompts,
     onAdd,
     onSubmit,
     isSubmitting,
   }: PromptFormProps) {
   ```
   Lepiej:
   ```tsx
   function PromptForm({ prompts, onAdd, onSubmit, isSubmitting }: PromptFormProps) {
   ```

2. **Nie rozpisuj trywialnej logiki na wiele linii** (z pustymi liniami i komentarzami do oczywistości). Prosty warunek może zostać w jednej–dwóch liniach.

   Lepiej:
   ```ts
   const isFutureDate = (value: string): boolean =>
     new Date(value).setHours(0, 0, 0, 0) > new Date().setHours(0, 0, 0, 0);
   ```

## Brak duplikacji

3. **Nie powtarzaj tego samego literału/obiektu w wielu miejscach** — wynieś do stałej i użyj wielokrotnie. Jeśli ten sam obiekt (np. domyślny kształt formularza) tworzysz kilka razy, zrób z niego jedną stałą:
   ```ts
   const EMPTY_PROMPT: PromptInput = { name: '', value: '', status: 'pending' };
   ```

4. **Jedno miejsce dostępu do zewnętrznej operacji.** Jeśli istnieje już funkcja/hook opakowujący wywołanie (np. `handleFetchData` wołające klienta API), odwołuj się do niego, a nie wołaj tej samej operacji niskopoziomowej wprost w wielu miejscach. Zmiana w jednym punkcie, brak rozjazdu.

## Struktura komponentu

5. **Słownik/mapa zamiast `switch`** przy prostym mapowaniu wartość → wynik:
   ```ts
   const VALUE_TYPE_LABELS: Record<number, string> = {
     0: 'Tekst',
     1: 'Liczba całkowita',
     2: 'Liczba dziesiętna',
     3: 'Wartość logiczna',
   };
   const getValueTypeLabel = (type: number): string => VALUE_TYPE_LABELS[type] ?? '';
   ```

6. **Hooki trzymaj razem** — na górze komponentu, jeden przy drugim, nie rozrzucone między funkcjami pomocniczymi (zgodne z Rules of Hooks — hooki tylko na najwyższym poziomie komponentu).

7. **Style oddzielone od renderu (TSX).** Nie mieszaj definicji stylów w środku JSX — trzymaj je osobno (CSS Modules lub inna konwencja stylowania ustalona dla repo) i podłączaj do komponentu. Render opisuje strukturę, nie definicje stylów. (W projekcie, z którego pochodzi ta reguła, robił to hook `withStyles` — w PromptQueue użyj konwencji stylowania przyjętej w tym repo.)

8. **Wspólne typy w osobnym pliku.** Gdy w obrębie feature masz kilka komponentów, identyczne definicje typów/interfejsów (a także wspólne stałe i helpery) wydzielaj do jednego współdzielonego modułu (np. `types.ts`), zamiast powielać je w każdym komponencie.

9. **Folder na komponent + współlokowany CSS Module.** Każdy komponent mieszka we własnym folderze nazwanym jak komponent (PascalCase), z plikiem komponentu i jego stylem obok siebie: `components/UI/Button/Button.tsx` + `components/UI/Button/Button.module.css`. Reużywalne komponenty pod `components/UI/`, komponenty domenowe w folderze feature'a — **pod-komponenty feature też dostają własny folder** (np. `features/prompts/PromptField/PromptField.tsx` + `PromptField.module.css`). Import stylu względny (`import styles from './Button.module.css'`); **bez barreli `index`** — importuj wprost plik komponentu (`.../UI/Button/Button`). Komponent bez stylów również ma własny folder. **Reguła uogólnia się: moduł z towarzyszącym plikiem dostaje własny folder grupujący** — komponent + `.module.css`, a także hook/klient + `.test.ts` (np. `hooks/useCreatePrompts/useCreatePrompts.ts` + `useCreatePrompts.test.ts`, `api/client/client.ts` + `client.test.ts`). Wszystkie custom hooki trzymaj pod `hooks/` (prefiks `use` → lokalizacja `hooks/`, nie rozrzucone po `features/`). Moduł bez towarzysza (np. `types.ts`, `prompts.ts`) zostaje pojedynczym plikiem. Bez barreli `index` — import wprost do pliku.

## TypeScript

- Typuj **propsy, stan i modele danych z API** (`interface` / `type`) — kontrakt komponentu ma wynikać z typów, nie z domysłów.
- **Unikaj `any`** — użyj konkretnego typu, `unknown` z zawężeniem albo generyka.
- Typy współdzielone między komponentami feature'a trzymaj w jednym module (patrz reguła 8).

## Czego ta reguła NIE narzuca

- Konkretnej biblioteki stylów, routingu, klienta HTTP — to decyzje projektowe (patrz `CLAUDE.md` / projekt techniczny z `/design`).
