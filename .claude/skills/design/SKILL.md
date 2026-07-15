---
name: design
description: "Projektowanie rozwiązania technicznego: system-architect generuje projekt, solution-critic go ocenia. Wynik zapisany do pliku .md z pytaniami do użytkownika. Można ponownie uruchomić z feedbackiem."
argument-hint: "[opis-lub-ścieżka-do-specyfikacji]"
---

# Projektowanie rozwiązania

## Flow

### Detekcja trybu
- Jeśli w `doc/projects/` istnieje plik pasujący do kontekstu — traktuj jako iterację (ponowne uruchomienie)
- W przeciwnym razie — pierwsze uruchomienie

### Ścieżka zapisu
Plik wynikowy: `doc/projects/<numer-taska>.md`. Jeśli folder `doc/projects/` nie istnieje — **utwórz go**.

### Pierwsze uruchomienie
1. Przeczytaj kontekst: $ARGUMENTS (może być opis tekstowy lub ścieżka do pliku).
2. **Architekt** — uruchom agenta narzędziem Task (agent: system-architect) z kontekstem:
   - Pełny opis/specyfikacja od użytkownika
   - Instrukcja: zaprojektuj rozwiązanie techniczne, uwzględnij pytania do użytkownika jeśli są potrzebne (np. wybór między podejściami). Zacznij plik od sekcji `## Cel`.
3. **Solution-critic** — uruchom agenta narzędziem Task (agent: solution-critic) z kontekstem: projekt techniczny od architekta. Instrukcja: oceń projekt, wydaj werdykt.
4. Zapisz wynik do `doc/projects/<numer-taska>.md`: weź projekt od architekta i dopisz/zaktualizuj sekcje audytowe na bazie wyniku krytyka. Format pliku — patrz „Format pliku projektu" niżej.
5. Pokaż użytkownikowi **całość** do weryfikacji:
   - Werdykt solution-critic (AKCEPTUJ / AKCEPTUJ Z UWAGAMI / PRZEPROJEKTUJ)
   - Kluczowe pytania do użytkownika (jeśli są)
   - Podsumowanie projektu

### Ponowne uruchomienie (z feedbackiem)
Jeśli użytkownik uruchomi `/design` ponownie z feedbackiem (np. odpowiedzi na pytania, uwagi do krytyki):
1. Przeczytaj istniejący plik projektu.
2. **Architekt (przeprojektowanie)** — uruchom system-architect z kontekstem:
   - Poprzedni projekt
   - Krytyka solution-critic
   - Feedback użytkownika
   - Instrukcja: przeprojektuj uwzględniając feedback.
3. Uruchom **solution-critic** na nowej wersji projektu od architekta.
4. Nadpisz plik projektu w `doc/projects/` nową wersją (projekt od architekta + sekcje audytowe od krytyka).
5. Pokaż użytkownikowi zaktualizowany wynik.

### Skondensowanie po werdykcie AKCEPTUJ (przed pełnym czytaniem)

Gdy solution-critic wyda **AKCEPTUJ** lub **AKCEPTUJ Z UWAGAMI bez blokerów** (projekt gotowy do `/implement`) — **proaktywnie zaproponuj skondensowanie audit-trailu** zanim użytkownik przejdzie do pełnego przeczytania projektu. Audit-trail (sekcje "Krytyka v1, v2, ..." + szczegółowa "Historia wersji") po werdykcie AKCEPTUJ traci wartość operacyjną — implementator czyta projekt po **decyzję**, nie po **proces**.

Wykonanie:
1. Policz **dokładnie** linie sekcji audytowych (`grep -n '^## Krytyka\|^## Historia'` + `wc -l` per sekcja). Jeśli stanowią ≥10% pliku — proponuj. Inaczej pomiń.
2. Pokaż 2-3 warianty redukcji z **rzeczywistą** estymatą linii opartą o policzony rozmiar audytu — **nie zgaduj**. Wariant radykalny ≈ usuwa cały audit + skraca Historię do ~3 linii; estymata redukcji = (linie audytu) − 3. Plus drobne korekty nagłówka (±2 linie).
3. Po wyborze użytkownika edytuj plik in-place i pokaż finalną liczbę linii vs estymatę. Jeśli wynik odbiega o >10% — wyjaśnij dlaczego (np. dodatkowe zmiany w trakcie iteracji).

Warianty:
- **Radykalny (zalecany)**: usunięcie wszystkich sekcji "Krytyka v*", skondensowanie "Historia wersji" do 1-2 zdań per wersja. Pełny audit-trail w git log (`git log -p doc/projects/<plik>.md`).
- **Umiarkowany**: każda Krytyka → 3-5 linii (werdykt + nazwy poprawek bez opisu); "Historia wersji" → 1-2 zdania per wersja.
- **Bez zmian**: pełen audit zostaje; po implementacji raport w `doc/implementation-reports/<task>.md` przejmie rolę audytową.

**Ostrzeżenie**: redukcja >25% bez cięcia sekcji merytorycznych (kod C#, Plan implementacji, Decyzje) jest **rzadko możliwa** — typowy projekt z 3 iteracjami ma audit ~15% pliku, więc realna granica redukcji to **-15% maksimum**. Nie obiecuj większej redukcji jeśli z liczenia wynika mniej.

Co **musi zostać** po skondensowaniu: wszystkie sekcje merytoryczne dla implementatora (Cel/Analiza, Architektura, Kontrakt API, Kluczowe abstrakcje z kodem, Przepływ danych, Kluczowe decyzje, Plan implementacji, Strategia testowania, Pytania otwarte). Co **może zostać usunięte**: szczegółowe Krytyki i wyliczenia poprawek per wersja.

## Format pliku projektu

```markdown
# Projekt: [Nazwa]

> Data: [YYYY-MM-DD]
> Wersja: [N]
> Werdykt: [AKCEPTUJ / AKCEPTUJ Z UWAGAMI / PRZEPROJEKTUJ]

## Cel
[1-3 zdania o biznesowym/technicznym celu rozwiązania]

## Proponowana architektura
[Podział odpowiedzialności, warstwy, komponenty]

## Elementy do zaimplementowania

| Element | Warstwa | Ścieżka |
|---------|---------|---------|
| [Nazwa] | API | `<ścieżka-w-repo>` |

## Przepływ danych
[Jak informacja przepływa między komponentami]

## Kluczowe decyzje
[Decyzje projektowe z uzasadnieniem]

## Pytania do użytkownika
- [?] [pytanie wymagające decyzji użytkownika]
- [?] [kolejne pytanie]

## Krytyka (solution-critic)
[Pełny raport z krytyki]

## Historia wersji
- v1: [opis zmian]
- v2: [opis zmian po feedbacku]
```

> **Zwięzłość projektu** (egzekwowane przez `system-architect` § 6 i `solution-critic` oś 7):
> - Każda informacja w jednym miejscu — z innych sekcji linkuj (`patrz § N`), nie powtarzaj
> - Kod C# > pseudokod > diagram ASCII (nie duplikuj flow w 2–3 formatach)
> - Plan implementacji = lista 1-zdaniowych etapów ze ścieżkami i numerami linii (uzasadnienia w innych sekcjach)
> - "Kluczowe decyzje" — tylko unikatowe wybory wymagające autoryzacji, nie streszczenie architektury
> - Bez podsekcji rozwijających pierwszy paragraf ("Trzy cele", "Pełna inwentaryzacja")
> - Bloki kodu: tylko `<summary>` ≤ 2 linie. Bez komentarzy inline (`//`), bez docstringów

---

$ARGUMENTS
