---
name: get-commit-message
description: "Generowanie zwiezlego opisu staged changes w Git — polski, krotka lista punktowa."
---

# Generowanie opisu commita

## Zasady
- Jezyk: polski
- Format: krotka lista punktowa
- Styl: tylko konkrety, bez zbednych slow
- Kazdy punkt: max 5-7 slow

## Instrukcja

1. Wykonaj `git diff --staged` aby pobrac zmiany
2. **Sprawdz spojnosc z dokumentacja projektu** (patrz sekcja ponizej)
3. Przeanalizuj zmiany i wypisz co zostalo dodane/usuniete/zmienione
4. Wygeneruj opis:

```
[<nazwa_branchu>] <glowna zmiana>

- punkt 1
- punkt 2
```

### Przykład

[pq-1] Dodano encję Prompt i migrację

- Encja Prompt + enum statusu
- Pierwsza migracja EF Core (PostgreSQL)

---

## Walidacja spojnosci z dokumentacja projektu

Przed wygenerowaniem opisu commita sprawdz, czy zmiany w kodzie sa spojne z dokumentacja projektowa.

### Krok po kroku

1. Wyciagnij numer ticketa z nazwy brancha (`git branch --show-current`):
   - Konwencja: `<projekt>/tasks/<ticket>/master` → wyciagnij `<ticket>` (np. `pq-1`, mala litera)
   - Jesli branch nie pasuje do wzorca — pomin walidacje

2. Sprawdz czy istnieja pliki:
   - `doc/projects/<ticket>.md` — design/zalozenia projektu
   - `doc/implementation-reports/<ticket>.md` — raport z implementacji

3. Jesli **zaden z plikow nie istnieje** — pomin walidacje (brak dokumentacji dla tego ticketa)

4. Jesli pliki istnieja — przeczytaj je i porownaj z `git diff --staged`:
   - Czy zmiany w kodzie **modyfikuja zalozenia projektowe** (np. zmiana endpointow, modeli, flow, architektury) ktore sa opisane w design?
   - Czy zmiany realizuja cos, co powinno byc odnotowane w raporcie implementacji?

5. Jesli wykryjesz **rozbieznosci** (kod zmienia cos, co design opisuje inaczej, lub implementacja nie jest odnotowana w raporcie):
   - Wylistuj konkretne rozbieznosci
   - Zapytaj uzytkownika: **"Czy zaktualizowac dokumentacje przed commitem?"**
   - Jesli tak — zaproponuj zmiany w odpowiednich plikach `.md`
   - Jesli nie — kontynuuj generowanie opisu commita

---

Przeanalizuj staged changes i wygeneruj opis.
