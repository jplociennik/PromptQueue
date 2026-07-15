# pq-6 — Orkiestracja (docker-compose) + dokumentacja

## Opis

**Tytuł:** Uruchomienie całego systemu jednym poleceniem i krótka instrukcja

Cały system — API, proces przetwarzający, baza, model i interfejs — uruchamia się jednym poleceniem, a krótka instrukcja wyjaśnia jak go postawić i z czego się składa. Ułatwia to uruchomienie środowiska i ocenę projektu.

## Projekt wstępny

Cel: spiąć wszystkie komponenty w jedno polecenie i dołączyć mini-dokumentację. Źródło: [DoD.md L9](DoD.md#L9) (mile widziana orkiestracja + krótka instrukcja).

Stack (za `CLAUDE.md`): docker-compose spinający backend API, worker, **PostgreSQL**, **Ollamę** i frontend. Zależy od [pq-1](pq-1.md)…[pq-5](pq-5.md) (usługi muszą istnieć; compose rośnie przyrostowo — Postgres pojawia się już w pq-1, Ollama w pq-3, tu domykamy całość).

Zakres:
- **Pełny docker-compose**: usługi `postgres`, `ollama` (z pobraniem modelu), `api`, `worker`, `frontend`; sieć wewnętrzna; kolejność startu (`depends_on` / healthchecki dla Postgres i Ollamy przed API/workerem).
- **Konfiguracja przez zmienne środowiskowe**: connection string do Postgresa, URL i nazwa modelu Ollamy.
- **Serwowanie frontu**: build statyczny (np. nginx) albo tryb podglądu Vite.
- **Mini-dokumentacja (README)**: wymagania, `docker compose up`, porty/URL-e, krótki opis każdego komponentu i jak je uruchomić osobno.

Definition of Done: `docker compose up` stawia całość; UI działa i rozmawia z API; worker przetwarza prompty przez Ollamę; README pozwala odtworzyć środowisko od zera.

## Do wyjaśnienia

- [?] Jak serwować frontend w compose — nginx ze statycznym buildem czy podgląd Vite?
- [?] Pobranie modelu Ollamy — automatycznie w compose (krok init, wydłuża pierwszy start) czy udokumentowany krok ręczny (ze względu na rozmiar/czas)?
