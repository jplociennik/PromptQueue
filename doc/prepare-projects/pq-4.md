# pq-4 — Frontend: setup + dodawanie promptów

## Opis

**Tytuł:** Interfejs do zgłaszania wielu promptów

Użytkownik może w przeglądarce wpisać i wysłać wiele promptów naraz do przetworzenia. To pierwszy element interfejsu — sam formularz dodawania; lista i statusy powstają w [pq-5](pq-5.md).

Endpoint konsumowany: `POST /api/v1/prompts`

## Projekt wstępny

Cel: szkielet aplikacji frontendowej + ścieżka dodawania promptów. Źródło: [DoD.md L7](DoD.md#L7) (React, dodawanie wielu promptów).

Stack (za `CLAUDE.md` → „Stack decisions"): **Vite + React + TypeScript** (SPA). Styl kodu wg skilla `code-frontend` (komponenty funkcyjne, kompozycja/SRP, logika w hookach, reużywalne komponenty). Zależy od [pq-2](pq-2.md) (kontrakt POST).

Zakres:
- **Setup projektu**: Vite + React + TS; struktura katalogów (reużywalne `components/ui`, komponenty domenowe przy feature, `hooks/`, warstwa `api/`).
- **Klient API**: pojedyncza warstwa wywołań HTTP (nie rozsypana po komponentach).
- **Formularz wielu promptów**: dodawanie/usuwanie pól lub wpis wielu promptów naraz, wysłanie przez `POST /api/v1/prompts`, walidacja pustych, feedback po wysłaniu.
- **Reużywalne komponenty** (np. `Input`, `Button`) zgodnie z zasadą kompozycji.

Definition of Done: aplikacja startuje na Vite; można dodać wiele promptów i wysłać je do API; puste pola są walidowane.

## Do wyjaśnienia

- [?] Konwencja stylowania — czysty CSS / CSS Modules / Tailwind / biblioteka UI (np. MUI)? Wpływa na regułę „style oddzielone od renderu" w skillu `code-frontend`.
- [?] Sposób wpisywania: dynamiczne pola, jedno pole textarea (prompt na linię), czy dodawanie po jednym?
