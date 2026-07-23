# Pytania rekrutacyjne (fullstack) — backend PromptQueue

Pytania, jakie można dostać na rozmowie na podstawie tego kodu, z krótkimi odpowiedziami „jak na interview". Podzielone tematycznie, od łatwiejszych do trudniejszych.

---

## 1. Architektura

**P: Opisz architekturę tego backendu. Dlaczego cztery projekty?**
O: Warstwy w stylu clean architecture: `Domain` (encje + porty, zero zależności), `Infrastructure` (EF Core/Npgsql — implementacja portów), `Api` i `Worker` jako dwa osobne hosty. Zależności idą tylko do środka — Domain nie wie nic o EF ani HTTP. Dzięki temu logikę domenową testuję bez bazy, a API i Worker mogą się deployować i skalować niezależnie.

**P: Czemu Worker to osobny proces, a nie `BackgroundService` w API?**
O: Osobny cykl życia — restart/deploy API nie przerywa przetwarzania promptów i odwrotnie. Osobne skalowanie (API skaluje się od ruchu HTTP, worker od długości kolejki) i izolacja awarii: crash workera nie kładzie API. W jednym procesie byłoby prościej, ale to świadomy trade-off.

**P: Jak API i Worker się komunikują?**
O: Nie komunikują się bezpośrednio — wspólna baza jest kolejką. API zapisuje prompt jako `Pending`, worker polluje tabelę po statusie. To wzorzec „database as a queue" — prosty, transakcyjny, bez dodatkowego brokera. Przy dużej skali zamieniłbym na RabbitMQ/Kafkę, ale tu broker byłby over-engineeringiem.

---

## 2. Domena / DDD

**P: Czemu reguły zmian statusu są zaszyte w encji `Prompt` (prywatne settery, metody `StartProcessing`/`Complete`/`Fail`), a nie w osobnym serwisie?**
O: Świadomie unikam anemicznego modelu domeny (Anemic Domain Model — antywzorzec: encja jako worek getterów/setterów, logika rozsiana po serwisach). Rich domain model: encja jest jedynym źródłem prawdy przejść stanu i sama pilnuje spójności — nie da się z zewnątrz zrobić `prompt.Status = Completed` z pominięciem walidacji, przejście z niedozwolonego stanu rzuca `InvalidOperationException` (strażnicy stanu, fail-loud). Serwis mógłby te reguły ominąć albo zduplikować; encji nie ominie nikt.

**P: Po co prywatny konstruktor bezparametrowy w `Prompt`?**
O: Dla EF Core — materializacja z bazy używa refleksji i potrzebuje konstruktora bez parametrów. Jest prywatny, żeby kod aplikacji musiał iść przez konstruktor publiczny, który wymusza niepusty content i ustawia `Id`, `Pending`, timestampy.

**P: Jakie stany ma prompt i które przejścia są legalne?**
O: `Pending → Processing → Completed | Failed`, plus `Requeue` (`Processing → Pending`) do recovery po przerwanym przetwarzaniu. `Fail` jest dozwolony z każdego stanu nieterminalnego; `Completed`/`Failed` są terminalne — nic z nich nie wychodzi.

**P: `Id = Guid.NewGuid()` w konstruktorze zamiast identity z bazy — dlaczego?**
O: Encja jest kompletna od urodzenia — API może zwrócić `Ids` w response POST bez czekania na rundę do bazy. W EF mapuję to jako `ValueGeneratedNever()`. Trade-off: losowe GUID-y fragmentują indeks klastrowany; w Postgresie PK to B-tree bez klastrowania danych, więc problem jest mniejszy, a przy skali użyłbym GUID v7 (sekwencyjny).

---

## 3. EF Core / baza

**P: Jak skonfigurowane jest mapowanie encji?**
O: Przez `IEntityTypeConfiguration<Prompt>` w osobnej klasie, nie adnotacjami na encji — domena zostaje czysta od EF. `Status` mapowany `HasConversion<string>()`.

**P: Status jako string w bazie, nie int. Trade-off?**
O: String jest czytelny w SQL-u i odporny na zmianę kolejności wartości enuma (int zapisuje pozycję — dopisanie wartości w środku enuma cicho psuje dane). Koszt: więcej bajtów i rename wartości wymaga migracji danych. Przy czterech statusach czytelność wygrywa.

**P: Kto i kiedy wykonuje migracje?**
O: Wyłącznie API, na starcie (`Database.MigrateAsync()`), fail-fast przy błędzie. Worker nigdy nie migruje — w compose startuje dopiero, gdy API jest healthy, więc nie ma wyścigu dwóch procesów o schemat. Przy wielu replikach API dodałbym osobny krok migracyjny (init job), bo `MigrateAsync` z wielu instancji naraz może się pokusić o konflikt.

**P: Po co repozytorium (`IPromptRepository`), skoro EF już jest abstrakcją?**
O: To port w Domain — domena i worker zależą od interfejsu, nie od EF. Ułatwia testy jednostkowe (in-memory fake zamiast DbContexta) i zbiera zapytania w jednym miejscu (deterministyczny `OrderBy(CreatedAt).ThenBy(Id)`). Świadomie cienkie — bez generycznego repo i unit-of-work wrappera, `SaveChangesAsync` jest po prostu wystawione.

**P: Czemu PostgreSQL, a nie SQLite/SQL Server?**
O: API i worker piszą do tej samej bazy równolegle — SQLite ma single-writer locking, więc odpada. SQL Server byłby OK, ale obraz Postgres alpine jest znacznie lżejszy w compose. Provider EF trzeba wybrać przed pierwszą migracją, bo migracje są provider-specific.

---

## 4. Worker / współbieżność

**P: `DbContext` jest scoped, a `BackgroundService` to singleton. Jak to pogodzić?**
O: Klasyczna pułapka — singleton nie może dostać scoped zależności wprost (captive dependency: DbContext żyłby wiecznie, a jego change tracker akumulowałby encje w nieskończoność — de facto wyciek pamięci). Worker wstrzykuje `IServiceScopeFactory` i tworzy scope na każdy cykl pętli, z niego bierze scoped `PromptProcessor` z repozytorium; po cyklu scope się zwalnia razem z DbContextem. Bonus: po wyjątku kolejny cykl dostaje świeży DbContext, bez zatrutego change trackera.

**P: Co się dzieje, gdy worker padnie (Ctrl+C, kill, prąd) w trakcie, gdy model generuje odpowiedź?**
O: Status `Processing` zapisuję w bazie **przed** wywołaniem modelu, więc po brutalnym przerwaniu prompt zostaje w bazie jako `Processing` — nie znika. Na starcie worker robi recovery: `RequeueInterruptedAsync` przestawia wszystkie `Processing` z powrotem na `Pending` i kolejka rusza dalej. Semantyka at-least-once — prompt może być wykonany drugi raz, ale nigdy nie zginie; dla LLM-a duplikat wykonania jest akceptowalny. Graceful shutdown (SIGTERM) idzie osobną ścieżką: `CancellationToken` przerywa pracę czysto.

**P: Czy ten design działa z wieloma instancjami workera?**
O: Nie w obecnej formie — dwa workery mogą pobrać ten sam `Pending` (race między SELECT a UPDATE) i recovery jednego cofnąłby prompty żywego drugiego. Świadome ograniczenie: jeden worker. Skalowanie: `SELECT ... FOR UPDATE SKIP LOCKED` do atomowego przejęcia zadania plus lease/heartbeat zamiast hurtowego requeue.

**P: Jak wygląda obsługa błędów wywołania modelu?**
O: Jeden retry po krótkim delayu. Jeśli i on padnie — prompt dostaje `Fail` z komunikatem (przyciętym do 2000 znaków) i pętla idzie dalej; zły prompt nie blokuje kolejki. Wyjątki infrastruktury (np. `SaveChanges`) celowo bąbelkują wyżej — łapie je pętla hosta, loguje i kolejny cykl rusza ze świeżym scope. `OperationCanceledException` przy shutdownie jest propagowany, nie maskowany jako błąd.

**P: Rola `CancellationToken` w tym kodzie?**
O: Przechodzi przez całą ścieżkę: HTTP request abort w API, `stoppingToken` w workerze — graceful shutdown przerywa `Task.Delay`, zapytania do bazy i call do modelu. Wzorzec `catch (OperationCanceledException) when (token.IsCancellationRequested)` odróżnia celowe anulowanie od faktycznego timeoutu.

**P: Polling co 5 s — czemu nie coś reaktywnego?**
O: Najprostsza rzecz, która spełnia wymagania; interwał konfigurowalny. Alternatywy: Postgres `LISTEN/NOTIFY` (push bez brokera) albo kolejka komunikatów. Polling ma przewidywalny koszt i zero dodatkowej infrastruktury — dobry default, dopóki latencja 5 s nie boli.

---

## 5. API

**P: Minimal API zamiast kontrolerów — dlaczego?**
O: Trzy endpointy — kontrolery nie wnoszą tu nic poza ceremoniałem. Handlery to statyczne metody z DI po parametrach, łatwo testowalne. Endpointy grupowane `MapGroup("/api/v1/prompts")` — wersjonowanie w ścieżce od pierwszego dnia.

**P: Jak działa walidacja i co zwraca przy błędzie?**
O: Ręczny statyczny walidator (limity: max 50 promptów, 8000 znaków po trim) zwracający słownik błędów per pole (`prompts[2]` → komunikat). Przy błędach `Results.ValidationProblem` → 400 z `ValidationProblemDetails` (RFC 7807/9457). Front dostaje maszynowo czytelną mapę błędów. FluentValidation byłby zasadny przy większej liczbie reguł; tu zewnętrzna zależność się nie broni.

**P: Obsługa nieprzewidzianych wyjątków?**
O: `IExceptionHandler` (nowość .NET 8+) zarejestrowany przez `AddExceptionHandler` + `AddProblemDetails`: loguje i zwraca 500 jako ProblemDetails z generycznym tytułem — szczegóły wyjątku nie wyciekają do klienta, każdy błąd API ma jeden spójny format.

**P: Czemu enumy serializują się jako `"pending"`, a nie `0`?**
O: `JsonStringEnumConverter` z camelCase policy. Kontrakt jest samoopisujący i stabilny (int zależy od kolejności w enumie), a frontendowy union type `'pending' | 'processing' | ...` mapuje się 1:1 bez tablicy tłumaczeń.

**P: DTO (`PromptResponse`) zamiast zwracania encji — po co?**
O: Encja domenowa nie wycieka poza granicę API — kontrakt HTTP jest oddzielony od modelu wewnętrznego, mogę zmieniać domenę bez łamania klientów. Rekordy C# jako DTO: zwięzłe, immutable.

**P: POST przyjmuje batch promptów. Co się stanie, gdy jeden z nich jest niepoprawny?**
O: Walidacja całego żądania przed jakimkolwiek zapisem — all-or-nothing. Wszystkie zapisy idą w jednym `SaveChangesAsync`, czyli jednej transakcji: nie ma częściowo zapisanego batcha.

---

## 6. Testy

**P: Jak przetestowane jest API?**
O: Dwupoziomowo. Unit testy walidatora — czysta logika, bez hosta. Testy integracyjne przez `WebApplicationFactory<Program>` (stąd `public partial class Program` na końcu Program.cs) z prawdziwym Postgresem z Testcontainers — testują pełny potok HTTP→EF→baza, nie mocka bazy.

**P: Czemu Testcontainers, a nie InMemory provider EF Core?**
O: InMemory nie jest relacyjną bazą — nie egzekwuje constraintów, ma inną semantykę transakcji i zapytań, więc test może przejść na czymś, co na Postgresie się wywali. A ten projekt używa cech konkretnego silnika: konwersja enuma na string (`HasConversion`), natywny typ `uuid`, `MaxLength` na statusie. Testcontainers stawia w locie prawdziwego, izolowanego Postgresa w Dockerze — testuję dokładnie to środowisko, które idzie na produkcję. Koszt: testy wymagają Dockera i są wolniejsze, dlatego szybka logika ma osobne unit testy.

**P: Jak testujesz workera bez prawdziwej Ollamy?**
O: Worker zależy od `IChatClient` (Microsoft.Extensions.AI) — w testach podstawiam `FakeChatClient` z zaprogramowanymi odpowiedziami/wyjątkami. Do unit testów procesora jest też `InMemoryPromptRepository`. Testuję logikę: przejścia stanów, retry, recovery — nie framework.

**P: Co daje projekt `TestSupport`?**
O: Współdzielone buildery testowe (`PromptBuilder`, builder requestów), fake'i i asercje ProblemDetails — DRY między projektami testowymi, testy czytają się jak scenariusze.

---

## 7. Docker / infrastruktura

**P: Jak wygląda orkiestracja startu? Skąd pewność, że worker nie ruszy przed bazą i modelem?**
O: Łańcuch `depends_on` z warunkami: postgres i ollama mają healthchecki; API czeka na `postgres: service_healthy`, a worker na `api: service_healthy` (API healthy = schemat zmigrowany) **i** `ollama-pull: service_completed_successfully` (model pobrany — one-shot kontener). Do tego worker ma własny runtime'owy wait na gotowość modelu, bo healthcheck to nie wszystko.

**P: Czemu Postgres na hoście wystawiony na 5433?**
O: Na maszynach dev często działa natywny Postgres na 5432 — mapping `5433:5432` unika konfliktu. Wewnątrz sieci compose kontenery dalej rozmawiają po 5432; różnica dotyczy tylko hosta.

**P: Skąd aplikacja bierze konfigurację i jak zachowuje się przy jej braku?**
O: `appsettings.json` + zmienne środowiskowe (env nadpisuje; konwencja `__` = `:`; w compose np. `ConnectionStrings__PromptQueue`, `Worker__OllamaModel`). Fail-fast: brak connection stringa albo `Worker:OllamaBaseUrl` rzuca przy starcie — lepiej ubić kontener natychmiast niż odkryć brak konfiguracji przy pierwszym requeście.

**P: Wymiana Ollamy na OpenAI/Claude — ile pracy?**
O: Mała — worker zna tylko `IChatClient` (Microsoft.Extensions.AI), `OllamaApiClient` to szczegół rejestracji w DI. Podmieniam jedną linię rejestracji na klienta OpenAI + konfiguracja klucza; logika przetwarzania nietknięta.

---

## 8. Pytania „gwiazdkowe" (senior / dyskusja)

**P: `DateTime.UtcNow` wprost w encji — co z tym nie tak?**
O: Utrudnia testowanie czasu (nie zamrozisz zegara) — czystszy design wstrzykuje `TimeProvider` (.NET 8+). Tu świadomy pragmatyzm: timestampy są informacyjne, żadna logika od nich nie zależy, więc abstrakcja czasu byłaby kosztem bez zysku. Wiedzieć kiedy warto — to jest sedno tego pytania.

**P: `GetAllAsync` zwraca całą tabelę. Kiedy to się zepsuje i co zrobisz?**
O: Przy tysiącach promptów response puchnie, a front polluje co kilka sekund. Rozwiązania po kolei: paginacja (limit/offset albo keyset po `CreatedAt, Id` — keyset stabilny przy wstawkach), filtrowanie po statusie, ewentualnie `If-None-Match`/ETag żeby polling nie przesyłał niezmienionych danych. Deterministyczny `ThenBy(Id)` już jest — to warunek sensownej paginacji.

**P: Gdzie w tym systemie może dojść do utraty lub podwójnego wykonania promptu?**
O: Utraty — nigdzie: każde przejście stanu jest persystowane przed kolejnym krokiem. Podwójne wykonanie — tak: worker pada po wywołaniu modelu, ale przed `SaveChanges` z `Complete` → recovery cofa do `Pending` → drugi przebieg. To at-least-once; exactly-once wymagałoby transakcyjnego outboxa albo idempotencji po stronie efektu, co dla odpowiedzi LLM nie ma sensu.

**P: Brakuje indeksu na `Status`, a worker filtruje po nim co 5 sekund. Problem?**
O: Przy małej tabeli seq scan jest tani i Postgres i tak by go wybrał. Przy wzroście: partial index `WHERE status = 'Pending'` — mały i idealny pod to zapytanie, bo prompty terminalne dominują w tabeli. Dobra odpowiedź pokazuje, że indeks to decyzja pod rozkład danych, nie odruch.

**P: Bezpieczeństwo — czego temu API brakuje do produkcji?**
O: Autentykacji/autoryzacji (każdy może POST-ować), rate limitingu (`AddRateLimiter` — jeden klient może zapchać kolejkę i GPU), HTTPS na brzegu, limitu rozmiaru body. Walidacja wejścia i brak wycieku szczegółów błędów już są. W demo to świadomie pominięte — ale trzeba umieć wymienić listę.
