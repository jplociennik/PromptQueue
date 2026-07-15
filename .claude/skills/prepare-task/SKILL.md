---
name: prepare-task
description: "Generowanie wstępnego projektu (zarys taska) z materiałów wejściowych: transkrypcji spotkania, screena MIRO, notatek, specyfikacji .md lub wklejonego tekstu. Wynik zapisywany do doc/prepare-projects/<numer-taska>.md — input dla /design. Użyj gdy chcesz przygotować zarys taska na podstawie materiałów ze spotkania, przekształcić notatki/transkrypcję/specyfikację w ustrukturyzowany projekt wejściowy. Triggeruj także gdy: użytkownik wspomina o transkrypcji, MIRO, notatkach ze spotkania, specyfikacji i chce z nich zrobić zarys projektu lub opis taska."
argument-hint: "<numer-taska> <ścieżki-plików-lub-tekst> [opis]"
---

# Przygotowanie wstępnego projektu z materiałów

Skill generuje **wstępny projekt** (zarys taska) na podstawie materiałów wejściowych — transkrypcji ze spotkania, screenów MIRO, notatek, specyfikacji `.md`, wklejonego tekstu. Wynik zapisywany do `doc/prepare-projects/<numer-taska>.md` jako **opcjonalny input dla `/design`**.

## Kiedy używać

- Materiały ze spotkania (transkrypcja, MIRO, notatki) są obszerne lub chaotyczne i potrzebujesz je przekształcić w ustrukturyzowany zarys, zanim ruszysz z `/design`.
- Chcesz mieć osobny artefakt „zarys taska" w `doc/prepare-projects/` — np. żeby uzgodnić zakres z osobą biznesową przed projektowaniem.

Jeśli materiały są zwięzłe i wystarczają do wprost uruchomienia `/design` — możesz pominąć ten skill i podać materiały bezpośrednio do `/design`.

## Argumenty

Użytkownik podaje:
- **numer taska** (wymagane) — np. `pq-1`. Używany do nazwy pliku wynikowego.
- **materiały wejściowe** (wymagane) — jedno lub więcej z:
  - ścieżki do plików: transkrypcje `.md`, screeny MIRO `.png`/`.jpg`, specyfikacje `.md`,
  - wklejony tekst bezpośrednio w prompcie (opis co zrobić, wymagania, notatki).
- **krótki opis** (opcjonalny) — 1–2 zdania o czym jest task; przydatne gdy materiały są obszerne.

## Flow

### 1. Wczytaj materiały

Odczytaj wszystkie podane materiały:
- **Pliki**: transkrypcje `.md`, screeny MIRO (obrazy), specyfikacje `.md`, notatki.
- **Tekst wklejony w prompt**: traktuj jak materiał wejściowy na równi z plikami.

Materiały mogą być bardzo różne — od długiej chaotycznej transkrypcji po krótki tekst „zrób X, Y, Z". Dopasuj podejście do tego, co dostałeś.

### 2. Poznaj kontekst projektu

Przeczytaj `CLAUDE.md`, żeby znać konwencje i strukturę projektu. Przejrzyj `doc/` — specyfikacje domenowe, które mogą dostarczyć kontekstu biznesowego. Nie zagłębiaj się nadmiernie — to nadal zarys, nie projekt techniczny, ale musisz rozumieć domenę na tyle, żeby poprawnie zidentyfikować kluczowe elementy.

### 3. Oceń rozmiar i dopytaj o niejasności

- Oszacuj ile znaków potrzeba na pokrycie kluczowych informacji.
  - Jeśli **≤ 2000 znaków** — generuj od razu.
  - Jeśli **> 2000 znaków** — poinformuj: „Projekt wymaga ok. X znaków (materiały są obszerne). Kontynuować z rozszerzonym projektem, czy skrócić do 2000?"
- Jeśli napotkałeś kwestie, które **blokują sensowne napisanie projektu** (nie wiesz co jest celem, nie rozumiesz kontekstu, materiały są sprzeczne) — **dopytaj użytkownika ZANIM wygenerujesz plik**. Nie generuj projektu z dużymi lukami.
- Drobniejsze niejasności (do dalszej analizy, do ustalenia z osobą trzecią) umieść w sekcji „Do wyjaśnienia" w wyniku — te nie blokują generowania.

### 4. Wygeneruj treść projektu wstępnego

Wyciągnij z materiałów i ustrukturyzuj następujące elementy (te, które są obecne — struktura jest **elastyczna**, dopasuj do tematu):

- **Cel biznesowy** — co ma powstać i dlaczego (1–2 zdania).
- **Opis mechanizmu** — jak to działa od strony biznesowej. Czerpaj z materiałów, ale też z bazy wiedzy projektu (`doc/`), jeśli mechanizm jest tam opisany.
- **Kluczowe elementy rozwiązania** — kontrakt API, model danych, sposób obsługi, integracje.
- **Reguły biznesowe i walidacje** — co musi być spełnione, jakie ograniczenia.
- **Decyzje architektoniczne** — jeśli na spotkaniu padły konkretne ustalenia techniczne (np. „robimy pull model", „dane w tabelach relacyjnych nie JSON"), uwzględnij je.
- **Definition of Done** — jeśli wynika z materiałów, opisz co musi być gotowe, żeby task uznać za zamknięty.
- **Ważne przy implementacji** — pułapki, edge case'y, zależności od zewnętrznych systemów, kolejność realizacji.

Pisz zwięźle, konkretnie, bez lania wody. Zarys ma zawierać **najważniejsze informacje** potrzebne, żeby `/design` mógł stworzyć szczegółowy projekt techniczny — ale sam nie jest szczegółowym projektem.

Treść projektu wstępnego ma typowy zakres **1500–2000 znaków** (większe przy obszernych materiałach — patrz krok 3).

### 5. Złóż plik

Zapisz plik `doc/prepare-projects/<numer-taska>.md` (np. `pq-1.md`) o strukturze:

```markdown
# <Tytuł projektu>

## Projekt wstępny

<treść projektu z kroku 4>

## Do wyjaśnienia

<lista kwestii do dopytania>
```

#### Sekcja „Do wyjaśnienia"

To kluczowa sekcja — zbiera **niedopowiedzenia, nierozstrzygnięte kwestie i tematy „do dalszej analizy"**:

- Rzeczy wprost oznaczone w materiałach jako „do ustalenia", „do dopytania", „nie wiemy jeszcze".
- Sprzeczności między różnymi wypowiedziami / materiałami.
- Kwestie wymagające decyzji biznesowej, gdy nie padła jednoznaczna odpowiedź.
- Zależności od osób trzecich (np. „Damian dopisze listę składników").
- Tematy świadomie odłożone na później (np. „defraudacje — na razie pomijamy").

**Nie zgaduj** odpowiedzi na te kwestie. Jeśli nie jesteś pewien czy coś zostało ustalone czy nie — dodaj tutaj. Lepiej dopytać niż przyjąć błędne założenie.

Każdy punkt w formie: `- [?] <pytanie/kwestia> — <kontekst skąd to wynika>`

Jeśli `doc/prepare-projects/` nie istnieje — utwórz katalog. Jeśli plik już istnieje — zapytaj, czy nadpisać.

### 6. Pokaż użytkownikowi całość do akceptacji

Wyświetl wygenerowany plik (projekt wstępny + Do wyjaśnienia) ze statystyką:

```
Projekt wstępny: ~Z znaków
```

Zapytaj: „Akceptujesz, czy chcesz coś zmienić?"

Jeśli użytkownik chce zmiany — popraw odpowiedni fragment i pokaż ponownie.

Po akceptacji **zakończ** — zapis pliku jest gotowy. **Nie commituj, nie pushuj** — git workflow użytkownik realizuje samodzielnie (gdy uzna za stosowne, użyje `/get-commit-message` i ręcznie zacommituje/wypchnie zmiany).

## Uwagi

- Wynik (`doc/prepare-projects/<numer-taska>.md`) jest **opcjonalnym** inputem dla `/design`. `/design` przyjmuje też wprost wklejony tekst lub plik z innej lokalizacji.

---

$ARGUMENTS
