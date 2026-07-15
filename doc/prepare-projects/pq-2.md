# pq-2 — API: dodawanie promptów + odczyt statusów

## Opis

**Tytuł:** Dodawanie wielu promptów i podgląd ich statusów przez API

System udostępnia API, przez które można zgłosić wiele promptów naraz do przetworzenia oraz odpytać o ich aktualny status i wynik. To kontrakt, z którego korzysta interfejs użytkownika.

Endpointy: `POST /api/v1/prompts`, `GET /api/v1/prompts`, `GET /api/v1/prompts/{id}`

Przykład:
```json
POST /api/v1/prompts
{ "prompts": ["Streść ten tekst...", "Przetłumacz na angielski..."] }

200 → { "ids": [1, 2], "status": "pending" }
```

## Projekt wstępny

Cel: warstwa HTTP do zgłaszania promptów i odczytu ich stanu. Źródło wymagań: [DoD.md L3](DoD.md#L3) (dodawanie + pobieranie stanów) i [DoD.md L7](DoD.md#L7) (lista dla frontu).

Stack: ASP.NET Core (Minimal API lub kontrolery) + EF Core/PostgreSQL. Zależy od [pq-1](pq-1.md) (encja `Prompt`, `DbContext`, repozytorium).

Zakres:
- **POST `/api/v1/prompts`** — przyjmuje listę promptów, zapisuje każdy jako `Pending`, zwraca identyfikatory. Cienka warstwa aplikacji nad repozytorium.
- **GET `/api/v1/prompts`** — lista wszystkich promptów (dla widoku listy we froncie, [pq-5](pq-5.md)) ze statusem i wynikiem.
- **GET `/api/v1/prompts/{id}`** — pojedynczy prompt: status + wynik/błąd.
- **Walidacja**: pusta lista → 400; pusty/za długi prompt → 400.
- **Kody HTTP**: POST 200/400; lista 200; po id 200/404.

Definition of Done: dodanie wielu promptów zwraca ich id i zapisuje je jako `pending`; oba GET-y zwracają aktualny stan; walidacja odrzuca puste wejście.

## Do wyjaśnienia

- [?] Limit liczby promptów w jednym żądaniu i maksymalnej długości pojedynczego promptu?
- [?] Lista promptów — pełna czy paginowana/filtrowana po statusie? (dla pollingu we froncie pełna wystarczy przy skali demo).
- [?] Konwencja JSON — domyślny camelCase (.NET) czy snake_case?
