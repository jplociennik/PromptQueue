---
name: implement
description: "Implementacja projektu technicznego: implementator pisze kod, code-reviewer przeprowadza przegląd. Pokazuje zaimplementowany kod i wynik CR. Można ponownie uruchomić z feedbackiem po review."
argument-hint: "[ścieżka-do-projektu-lub-opis]"
---

# Implementacja projektu

## Kiedy używać
- `/implement` — implementacja na podstawie `/design` lub opisu tekstowego

## Flow

### Pierwsze uruchomienie (TDD: Red → Green → Review)
1. Przeczytaj projekt/specyfikację: $ARGUMENTS (ścieżka do pliku .md z projektem lub opis)
2. **RED — Napisz testy** narzędziem Task (agent: implementator) z kontekstem:
   - Pełny projekt/specyfikacja
   - Instrukcja: napisz testy integracyjne wg specyfikacji — endpointy, happy path, error paths. Testy muszą się **kompilować** (użyj minimalnych stubów jeśli trzeba: pusty endpoint zwracający 501, puste DTO), ale **nie przechodzić** (brak logiki biznesowej)
   - Uruchom testy (`dotnet test`) — potwierdź że są na czerwono
3. **GREEN — Implementuj** narzędziem Task (agent: implementator) z kontekstem:
   - Pełny projekt/specyfikacja
   - Skille do załadowania (jeśli wymienione w projekcie)
   - Instrukcja: zaimplementuj kod produkcyjny tak, aby testy z kroku 2 przeszły
   - Uruchom testy (`dotnet test`) — potwierdź że są na zielono
4. Uruchom agenta narzędziem Task (agent: code-reviewer) z kontekstem:
   - Lista zaimplementowanych/zmodyfikowanych plików (testy + kod produkcyjny)
   - Projekt jako referencja
   - Instrukcja: przeprowadź przegląd kodu
5. Zapisz raport do `doc/implementation-reports/<numer-taska>.md` (utwórz `doc/implementation-reports/` jeśli nie istnieje).
6. Pokaż użytkownikowi:
   - Co zostało zaimplementowane (lista plików z krótkim opisem)
   - Wynik code review (problemy krytyczne, ostrzeżenia, sugestie)
   - Zalecane działania (jeśli są problemy)

### Ponowne uruchomienie (z feedbackiem po CR)
Jeśli użytkownik uruchomi `/implement` ponownie z uwagami z code review:
1. Przeczytaj feedback użytkownika (np. "napraw problemy krytyczne z CR", "uwzględnij sugestie X i Y")
2. Uruchom implementator z kontekstem:
   - Poprzedni code review
   - Feedback użytkownika
   - Instrukcja: napraw wskazane problemy
3. Uruchom code-reviewer na poprawionym kodzie
4. **Zaktualizuj implementation report** — oznacz rozwiązane uwagi (patrz: Aktualizacja raportu po zmianach)
5. Pokaż użytkownikowi zaktualizowany wynik

## Raport implementacji

Raport opisuje **stan finalny** implementacji — nie podróż przez iteracje. Historia git ma motywacje refaktorów; raport ma być przydatny dla code reviewera i osoby wchodzącej w temat tygodnie później. Cel: w 5 minut wiedzieć **co**, **gdzie** i **dlaczego** się zmieniło, oraz **co budzi wątpliwości**.

### Struktura

````markdown
# Raport implementacji: <Numer> — <Krótki opis taska>

> Projekt: [doc/projects/<Numer>.md](../projects/<Numer>.md)
> Data: <YYYY-MM-DD> → <YYYY-MM-DD> (<N> iteracji)
> Commit: `<skrót> <tytuł>`
> MR: [<grupa>/<repo>!<NN>](<link>)
> Code review: <N>× chodził (iter. <X>, <Y>, <Z>). <Status po ostatnich zmianach — np. "Po iter. N nie był uruchamiany — opis czego dotyczyła">

## Co realizuje task

<2-3 zdania o celu biznesowym/technicznym. Jakie endpointy/zachowanie wchodzą, co świadomie zostaje na potem.>

## Stan implementacji vs. projekt (opcjonalne — gdy projekt v* miał kilka decyzji do realizacji)

| Decyzja projektu | Stan |
|------------------|------|
| <decyzja 1> | ✅ <plik / sposób realizacji> |
| <decyzja 2> | ⏳ <odłożone — dlaczego, do kiedy> |

## Zakres zmian

### <Warstwa 1, np. Backend (nowy moduł X)>

| Plik | Op | Opis |
|------|----|------|
| [<ścieżka>](<link względny>) | NEW/MOD | <co to robi w 1 linii> |

### <Warstwa 2>

(analogicznie)

### Testy

| Plik | Op | Pokrycie |
|------|----|----------|

## Wyniki testów

| Projekt | Passed | Failed | Skipped |
|---------|--------|--------|---------|

`dotnet build`: <wynik> (np. **0 errors, 0 warnings**).

## Werdykt CR (po ostatnim przeglądzie)

**<AKCEPTUJ / AKCEPTUJ Z UWAGAMI / ODRZUCAM>** — N krytycznych, N ostrzeżeń (N naprawionych, N akceptowanych), N sugestii.

| # | Plik | Opis | Status |
|---|------|------|--------|
| Ś1 | <plik> | <opis> | ✅ naprawione w iter. X / ⏳ akceptowane: <powód> / 🔴 pending |

## Wątki do uwagi przy CR (kontrowersyjne decyzje)

<Tu trafiają decyzje które były flip-flopowane, łamiące konwencję projektu, lub które reviewer może zechcieć zakwestionować. 2-3 zdania per wątek, z linkiem do pliku i ew. komentarza w MR.>

1. **<temat>** ([plik:linia](<link>)) — <decyzja + jaki jest alternatywny argument>. <Status: do dyskusji w CR / zaakceptowane mimo wątpliwości>.

## Follow-up (poza scope)

- **<typ — osobny task / sugestia / odłożone>**: <co i dlaczego>

## Historia iteracji (skrócona)

| # | Co | CR? |
|---|----|-----|
| 1 | Initial implementation | ✅ AKCEPTUJ Z UWAGAMI |
| 2 | Naprawa Ś2-Ś5 z CR (przeniesienie enum do Data/, parametryzacja SQL, +1 test mappera) | ✅ AKCEPTUJ |
| 3 | <Krótki opis refaktoru — co się zmieniło, bez motywacji> | — |
| ... | ... | — |
| N | <opis> | ✅/❌/— |
````

### Zasady tworzenia raportu

- **Stan finalny, nie podróż** — git history ma motywacje refaktorów; raport ma działać przy CR
- **Bez `~~strikethrough~~`** rozwiązanych uwag — usuwamy je z tabeli lub zmieniamy status na `✅`, bez historycznych blokad
- **Bez sekcji `## Iteracja N — ...`** z motywacjami i tabelami zmian — refaktor → **1 wiersz** w tabeli Historia iteracji. Motywacje idą do commit messages
- **Wątki kontrowersyjne** — 2-3 zdania per wątek, z linkiem do pliku/linii i ew. komentarza w MR. Nie pełne kopie tabel zmian
- **Linki do plików** — `[ścieżka](../../plik)` żeby reviewer kliknął i przeszedł

## Aktualizacja raportu po zmianach

Gdy są kolejne uruchomienia `/implement` lub poprawki w rozmowie — **aktualizuj stan finalny**, nie dodawaj sekcji iteracji:

1. **Werdykt CR** — w tabeli uwag zmień status resolved na `✅ naprawione w iter. X` lub usuń wiersz (jeśli uwaga była drobna). Nie zostawiamy strikethrough.
2. **Zakres zmian** — jeśli iteracja dotknęła nowych plików → dopisz wiersz. Jeśli zmieniła charakter istniejącego pliku → zaktualizuj. Jeśli plik został usunięty/wycofany → usuń wiersz.
3. **Wyniki testów** — nadpisz tabelę aktualnym stanem (nie trzymamy obok siebie starych i nowych liczb).
4. **Wątki kontrowersyjne** — jeśli iteracja wprowadziła decyzję która budzi wątpliwości, była przedmiotem flip-flopa, lub łamie konwencję projektu → 2-3 zdania w tej sekcji (z linkiem do komentarza w MR, jeśli był).
5. **Historia iteracji** — **1 wiersz w tabeli** (kolumny: #, Co, CR?). Kolumna „CR?" = `✅ <werdykt>` jeśli po tej iteracji chodził `code-reviewer`, `—` jeśli nie. Po iteracji bez CR nagłówek raportu (linia „Code review:") powinien to zaznaczyć.
6. **Metadane w nagłówku** — aktualizuj „Ostatnia aktualizacja", liczbę iteracji, status code review.

### Co NIE wchodzi do raportu

- Pełne stack trace'y błędów buildu / testów (są w CI logs)
- Krok-po-kroku opis każdej iteracji — od tego są commit messages
- Motywacje refaktorów które zostały zrealizowane bez kontrowersji (tylko 1 wiersz w historii: „Co", bez „Dlaczego")
- Tabele zmian z `git diff` per iteracja — od tego jest `git log -p`
- Pliki auto-generowane (np. EF Designer/Snapshot) — wymień raz, bez detali

## Reguły

- **TDD**: testy przed implementacją — Red (testy nie przechodzą) → Green (implementacja przechodzi testy)
- Kompilacja po implementacji: `dotnet build <ścieżka-do-csproj>` — jeśli się nie kompiluje, napraw przed code review
- Testy po implementacji: `dotnet test` — jeśli nie przechodzą, napraw przed code review
- Code review zawsze na końcu — nawet jeśli nie ma oczywistych problemów
- Przy ponownym uruchomieniu napraw TYLKO wskazane problemy (nie refaktoryzuj całego kodu)
- Po rozwiązaniu uwag z CR — zawsze zaktualizuj implementation report (patrz: Aktualizacja raportu po zmianach)

---

$ARGUMENTS
