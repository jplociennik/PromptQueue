# pq-3 — Worker + integracja z modelem językowym (Ollama)

## Opis

**Tytuł:** Automatyczne przetwarzanie promptów w tle przez model językowy

Zgłoszone prompty są przetwarzane asynchronicznie w tle przez lokalny model językowy, a ich status zmienia się od oczekującego, przez przetwarzany, aż do zakończonego lub nieudanego. Użytkownik nie czeka na wynik w trakcie dodawania i widzi postęp każdego zgłoszenia.

## Projekt wstępny

Cel: silnik przetwarzania — osobny proces realizujący cykl życia zadania. Źródło: [DoD.md L5](DoD.md#L5) (oddzielny proces, biblioteka do LLM, stany oczekujące/przetwarzane/zakończone/nieudane).

Stack (za `CLAUDE.md`): worker jako osobny proces (.NET `BackgroundService`), **Ollama** (model lokalny) przez abstrakcję **`Microsoft.Extensions.AI` (`IChatClient`)** — provider wymienialny. Zależy od [pq-1](pq-1.md) (domena/baza); [pq-2](pq-2.md) pomocniczo (żeby były prompty; da się testować wstawiając ręcznie).

Zakres:
- **Pętla przetwarzania**: pobranie promptów `Pending` (polling bazy w interwale), oznaczenie `Processing`, wywołanie modelu, zapis wyniku i `Completed` albo błędu i `Failed`.
- **Odporność**: błąd/timeout modelu → `Failed` z komunikatem (worker nie wywala się); prompt raz `Completed` nie jest przetwarzany ponownie (idempotencja).
- **Ollama w docker-compose**: usługa + pobranie modelu; konfiguracja (nazwa modelu, base URL, interwał) przez zmienne środowiskowe.

Definition of Done: dodany prompt samoczynnie przechodzi `pending → processing → completed` z wynikiem od modelu; błąd modelu skutkuje `failed` z komunikatem, a nie awarią procesu.

## Do wyjaśnienia

- [?] Domyślny model Ollama (kompromis rozmiar/jakość/zasoby, np. `llama3.2`, `qwen2.5`, `phi3`)?
- [?] Przetwarzanie sekwencyjne czy równoległe (kilka promptów naraz)? DoD nie precyzuje.
- [?] Prompty utknięte w `processing` po restarcie workera — resetować do `pending`?
- [?] Ponawianie przy błędzie modelu, czy od razu `failed`?
