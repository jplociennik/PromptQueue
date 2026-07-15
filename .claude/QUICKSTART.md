# QUICKSTART — skille i agenci w codziennej pracy

---

## Flow pracy (input → design → implementacja → commit)

```
materiały (transkrypcja, MIRO, notatki, specyfikacja, wklejony tekst)
    → /prepare-task <numer> <materiały>   ─ opcjonalny zarys w doc/prepare-projects/<numer>.md
    → /design  ─ system-architect → projekt techniczny
               ─ równolegle: solution-critic 
    → /implement  ─ TDD: testy → kod → code-reviewer
    → /get-commit-message  ─ generuje opis commita
    → ręczny commit + push (poza skillami)
```

### 1. (Opcjonalnie) `/prepare-task <numer-taska> <materiały>`

- Użyj, gdy materiały wejściowe są obszerne lub chaotyczne (transkrypcja ze spotkania, screen MIRO, notatki, specyfikacja) i chcesz najpierw przekształcić je w ustrukturyzowany zarys.
- Wynik: `doc/prepare-projects/<numer-taska>.md` z sekcjami: `## Projekt wstępny` i `## Do wyjaśnienia`.
- Plik jest **opcjonalnym** inputem dla `/design`.
- **Bez gita** — skill nic nie commituje ani nie pushuje. Jeśli chcesz wypchnąć zarys, użyj `/get-commit-message` i zrób to ręcznie.

### 2. `/design`

- Input: ścieżka do pliku `.md` (np. z `doc/prepare-projects/<numer>.md` po `/prepare-task`, z innego repo lub utworzonego ręcznie) **lub** wklejony tekst.
- Najpierw **system-architect** → projekt techniczny.
- Następnie **solution-critic** ocenia projekt (werdykt: AKCEPTUJ / AKCEPTUJ Z UWAGAMI / PRZEPROJEKTUJ).
- Wynik: `doc/projects/<numer-taska>.md` — projekt techniczny razem z wynikiem krytyki.
- Programista weryfikuje całość. Iteracja: ponowne `/design` z feedbackiem przeprojektowuje rozwiązanie.

### 3. `/implement`

- Flow TDD: **RED** (implementator pisze testy wg designu, `dotnet test` → failują) → **GREEN** (implementator pisze kod produkcyjny, `dotnet test` → przechodzą) → **code-reviewer**.
- Programista sprawdza diffy, testy, raport.

### 4. `/get-commit-message`

- Po implementacji (i po rozwiązaniu wszystkich uwag z CR) ręcznie wywołujesz skill — proponuje treść commit message z numerem taska Jira na podstawie staged changes.
- **Sam commit i push robisz ręcznie** (skill tylko proponuje treść — nie commituje, nie pushuje).

---

## Warianty

| Sytuacja | Flow |
|----------|------|
| Materiały ze spotkania → pełen cykl | `/prepare-task` → `/design` → `/implement` → `/get-commit-message` → ręczny commit/push |
| Gotowy opis / specyfikacja → pełen cykl | `/design` → `/implement` → `/get-commit-message` → ręczny commit/push |
| Szybka zmiana z jasnym zakresem | `/implement [opis]` (bez `/design`) → `/get-commit-message` → ręczny commit/push |

---

## Skille użytkownika (`/nazwa`)

| Skill | Kiedy |
|-------|--------|
| `/prepare-task` | Zarys taska z materiałów wejściowych (transkrypcja, MIRO, notatki, spec) → `doc/prepare-projects/<numer>.md`. Bez gita |
| `/design` | Projekt techniczny — system-architect projektuje, solution-critic ocenia |
| `/implement` | Implementacja TDD (testy → kod → code-reviewer)
| `/get-commit-message` | Propozycja opisu commita (staged) — sam commit i push robisz ręcznie |

---

## Agenci

| Agent | Rola | Wywoływany przez |
|-------|------|-----------------|
| `system-architect` | Projektowanie architektury | `/design` |
| `solution-critic` | Krytyczna ocena rozwiązań | `/design` |
| `implementator` | Pisanie kodu produkcyjnego | `/implement` |
| `code-reviewer` | Przegląd kodu | `/implement` |

---

## Skille referencyjne (ładują agenci automatycznie)

| Skill | Warstwa | Kluczowe tematy |
|-------|---------|-----------------|
| `code-frontend` | React (Vite, TS) | Styl kodu komponentów: czytelność zapisu, brak duplikacji, słowniki zamiast switchy, hooki razem, separacja stylów od renderu, wspólne typy w osobnym pliku, minimalizm |

Skille referencyjne (`user-invocable: false`) nie są uruchamiane przez `/`, tylko ładowane przez agentów (`system-architect`, `implementator`, `code-reviewer`) podczas pracy nad daną warstwą.

---

## Struktura dokumentacji

```
doc/
├── prepare-projects/          # Wstępne projekty (zarys taska) — opcjonalny input dla /design
├── projects/                  # Projekty techniczne z /design
├── implementation-reports/    # Raporty implementacji z /implement
└── doc-*.md                   # Dokumenty techniczne

.claude/
├── QUICKSTART.md              # ten plik — codzienna praca
├── skills/                    # definicje skilli
└── agents/                    # definicje agentów
```
