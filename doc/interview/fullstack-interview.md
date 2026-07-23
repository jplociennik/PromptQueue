# PromptQueue — pytania rekrutacyjne na rozmowę fullstack

Pytania, jakie można dostać na rozmowie na podstawie kodu projektu PromptQueue, z krótkimi odpowiedziami „jak na interview". Część I — backend (C#/.NET), część II — frontend (React/TypeScript).

# Część I — Backend (C# / .NET)


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

# Część II — Frontend (React / TypeScript)


## 1. Architektura frontendu

**P: Opisz strukturę katalogów. Czym kierowałeś się przy podziale?**
O: Cztery warstwy: `api/` (klient HTTP + typowane kontrakty), `components/UI/` (czyste, reużywalne prezentacyjne: Button, TextArea, Alert, StatusBadge), `features/prompts/` (komponenty domenowe składające UI), `hooks/` (cała logika stanu i side-effectów). Komponenty featurowe nie gadają z API bezpośrednio — robią to hooki. Każdy komponent/hook to folder z kolokowanym testem i `.module.css` — wszystko, co go dotyczy, jest obok.

**P: Czemu Vite + React SPA, a nie Next.js?**
O: Backend w C# jest właścicielem danych i API, SEO/SSR nie występuje, a odświeżanie to client-side polling — SSR nic by nie dał, a dodałby serwer Node do orkiestracji. SPA serwowane przez nginx to najprostszy fit. Umiem uzasadnić, kiedy bym to zmienił: wymóg SEO albo SSR.

**P: Nie widzę Reduxa ani React Query. Jak zarządzasz stanem i czemu bez bibliotek?**
O: Stan serwerowy żyje w dwóch custom hookach na `useReducer` (`usePromptPolling`, `useCreatePrompts`), stan formularza w trzecim (`usePromptFields`). Przy jednym widoku i jednym zasobie biblioteka to koszt bez zysku. React Query wchodzi, gdy pojawia się wiele zasobów, cache między widokami, deduplikacja żądań — wtedy sam bym po niego sięgnął, bo mój polling to ręczna implementacja wycinka tego, co on daje.

---

## 2. Hooki / cykl życia

**P: W `usePromptPolling` użyłeś rekursywnego `setTimeout`, a nie `setInterval`. Dlaczego?**
O: `setInterval` odpala żądania sztywno co X sekund, nie interesując się, czy poprzednie wróciło — przy wolnym backendzie żądania nakładają się i zalewają serwer (request piling). Rekursywny `setTimeout` planuje następny tick dopiero po zakończeniu poprzedniego (sukcesem lub błędem), więc w locie jest zawsze co najwyżej jedno żądanie, a interwał to gwarantowana przerwa między żądaniami.

**P: Co się dzieje z pollingiem, gdy komponent się odmontuje w trakcie żądania?**
O: Cleanup `useEffect` robi trzy rzeczy: `cancelled = true` (strażnik — nawet jeśli promise się rozwiąże, nie będzie `dispatch` na odmontowanym komponencie ani kolejnego timera), `controller.abort()` (ubija żądanie in-flight na poziomie sieci przez `AbortController`), `clearTimeout` (kasuje zaplanowany tick). Flaga i abort się uzupełniają: abort anuluje sieć, flaga chroni przed późnym `dispatch`, gdy response zdążył wrócić.

**P: Jak działa `refetch`? Widzę tam licznik `trigger` — po co ten trik?**
O: `refetch` inkrementuje stan `trigger`, który jest zależnością `useEffect` — zmiana restartuje cały efekt: cleanup ubija stary cykl (timer + żądanie), nowy zaczyna od natychmiastowego ticka. Dostaję „odśwież teraz" bez duplikowania logiki i bez ryzyka dwóch równoległych pętli. Używa tego formularz po udanym POST, żeby nowe prompty pojawiły się na liście od razu, a nie po 2 s.

**P: Aplikacja jest w `StrictMode`. Co on robi z twoim efektem pollingu i czemu to działa?**
O: W dev StrictMode montuje komponent podwójnie: mount → cleanup → mount, żeby wykryć efekty bez sprzątania. Mój efekt to przeżywa, bo cleanup jest kompletny — pierwszy cykl zostaje w pełni ubity (flaga, abort, clearTimeout), zanim ruszy drugi. Gdyby cleanupu brakowało, miałbym dwie pętle pollingu i to jest dokładnie ta klasa bugów, którą StrictMode ma ujawniać.

**P: Czemu `useReducer` zamiast kilku `useState`?**
O: Stan pollingu (`prompts`+`status`+`error`) zmienia się zawsze razem i przejścia są zdarzeniami (`success`/`error`) — reducer trzyma te przejścia w jednej czystej funkcji, którą testuję bez renderowania. Przy trzech powiązanych `useState` łatwo o niespójny stan pośredni. Formularz (`usePromptFields`) analogicznie: operacje add/remove/change/reset na liście pól to naturalne akcje.

**P: Po błędzie pollingu lista promptów nie znika. Celowe?**
O: Tak — reducer przy `error` robi `{...state, status:'error'}`, zachowując ostatnie dane (stale-while-error). Użytkownik widzi listę i pasek błędu zamiast pustego ekranu, a kolejny tick i tak leci — polling sam się leczy po przejściowym błędzie sieci.

---

## 3. Formularz / komponenty

**P: Skąd `key={field.id}` z `crypto.randomUUID()`, a nie index?**
O: Pola można usuwać ze środka listy. Przy `key={index}` po usunięciu pola React skleja stan DOM (fokus, pozycja kursora) z niewłaściwym polem, bo indeksy się przesuwają. Stabilny identyfikator nadany przy utworzeniu pola jednoznacznie wiąże element z jego danymi przez cały cykl życia.

**P: Czemu nie da się usunąć ostatniego pola i gdzie ta reguła siedzi?**
O: W reducerze (`remove` zwraca stan bez zmian przy `length <= 1`), nie w komponencie — reducer jest SSOT reguł listy pól. Powód praktyczny: pusta lista → pusty POST → gwarantowane 400. Reguła w reducerze jest przetestowana jednostkowo i żaden komponent jej nie obejdzie.

**P: Jak zbudowany jest `TextArea`? Co daje `extends TextareaHTMLAttributes`?**
O: Przezroczysty wrapper: rozszerzam natywne propsy o `invalid`, resztę przekazuję przez `{...rest}` — komponent przyjmuje wszystko, co natywna textarea (placeholder, disabled, onChange), bez ręcznego przepisywania propsów. `invalid` steruje klasą CSS i `aria-invalid` — stylowanie i dostępność z jednego propa.

**P: W CSS textarea jest `field-sizing: content`. Co to robi?**
O: Natywna właściwość CSS (Chromium 123+): textarea sama rośnie i kurczy się z treścią — zero JS, zero mierzenia scrollHeight, zero bibliotek. `min/max-height` ograniczają zakres i robią za fallback dla przeglądarek bez wsparcia, a `resize: vertical` zostaje jako ręczna furtka. Progressive enhancement: tam, gdzie property nie działa, textarea jest po prostu stała, ale w pełni używalna.

**P: `StatusBadge` używa słownika `Record<PromptStatus, string>` zamiast switcha. Różnica?**
O: `Record` po unii wymusza kompletność w compile-time — gdy backend dostanie piąty status i poszerzę union, TypeScript nie skompiluje słownika bez nowego klucza. Switch bez `default` z exhaustive checkiem tego nie da, a z `default` cicho przepuści. Krócej i bezpieczniej.

**P: Walidacja pustych pól odpala się dopiero po pierwszej próbie wysłania. Czemu?**
O: Flaga `submitAttempted` — świadomy wybór UX: nie krzyczę czerwoną ramką na użytkownika, który dopiero otworzył formularz z pustym polem. Po pierwszym submit walidacja jest już „live", bo `invalid` liczy się z aktualnej wartości przy każdym renderze.

---

## 4. Warstwa API / TypeScript

**P: Jak frontend obsługuje błędy z API? Widzę interceptor axiosa.**
O: Backend zwraca wszystkie błędy jako ProblemDetails (RFC 7807). Interceptor mapuje każdy `AxiosError` na własny `ApiError` (status, message z `title`, `validationErrors` z `errors`) — reszta aplikacji zna jeden typ błędu i nie wie nic o axiosie. Samo mapowanie to czysta funkcja `toApiError`, testowalna bez sieci. Status `0` oznacza błąd sieciowy — wtedy pokazuję generyczny komunikat po polsku zamiast surowego „Network Error".

**P: Jak zapewniasz zgodność typów frontu z kontraktem backendu?**
O: Ręcznie utrzymywane typy w `api/types.ts` lustrzane do C# recordów; statusy to union `'pending' | 'processing' | ...`, bo backend serializuje enumy jako camelCase stringi — mapowanie 1:1 bez tłumaczeń. Przy większym API wygenerowałbym typy z OpenAPI, żeby dryf kontraktu wykrywał się sam; przy trzech endpointach ręczne typy są tańsze.

**P: Co robi `satisfies CreatePromptsRequest` w `prompts.ts`?**
O: Sprawdza zgodność literału z typem bez rzutowania i bez poszerzania typu — w odróżnieniu od `as`, który by przemilczał brakujące pole, i od anotacji `:`, która utrąca wnioskowanie. Literówka w nazwie pola requesta to błąd kompilacji.

---

## 5. Testy

**P: Jak testujesz hook z timerami i siecią, jak `usePromptPolling`?**
O: `renderHook` z Testing Library, `vi.mock` na module API (hook nie wie, że nie ma sieci), `vi.useFakeTimers` + `advanceTimersByTime(2000)` — testuję kolejne ticki bez realnego czekania. Kluczowe przypadki: pierwszy fetch, re-schedule po interwale, zachowanie danych przy błędzie i samowyleczenie, oraz unmount — asercja, że po odmontowaniu licznik wywołań nie rośnie i `signal.aborted === true`.

**P: Czemu Vitest, a nie Jest?**
O: Ten sam pipeline transformacji co Vite (config w jednym `vite.config.ts`, blok `test`), natywne ESM i TS bez babela, kompatybilne API. W projekcie na Vite Jest wymagałby osobnej konfiguracji transformacji tylko po to, żeby zrobić to samo wolniej.

**P: Testujecie implementację czy zachowanie?**
O: Zachowanie: testy hooków patrzą na zwracany stan i wywołania API, testy komponentów (Testing Library) na to, co widzi użytkownik. Reducery testuję jako czyste funkcje — wejście/wyjście. Nie asertuję szczegółów wewnętrznych, więc refaktor implementacji nie kładzie testów.

---

## 6. Build / deployment

**P: Opisz Dockerfile frontendu. Czemu multi-stage?**
O: Stage 1: `node:20-alpine`, `npm ci`, `npm run build` (tsc + vite). Stage 2: `nginx:1.27-alpine` dostaje tylko `dist/` i konfig. W obrazie produkcyjnym nie ma Node, node_modules ani źródeł — mniejszy obraz i mniejsza powierzchnia ataku. `npm ci` zamiast `install`: instaluje dokładnie z lockfile'a, fail przy dryfie — powtarzalny build.

**P: Do czego służy `try_files $uri $uri/ /index.html` w nginx?**
O: SPA fallback: żądanie, które nie trafia w istniejący plik statyczny, dostaje `index.html`, a routing przejmuje JS. Bez tego deep-link albo refresh na podścieżce dawałby 404 z nginxa. Tu jest jeden widok, ale to standard, który nie może zniknąć w dniu dodania routera.

**P: Frontend woła API przez `/api` na własnym originie zamiast bezpośrednio. Dlaczego?**
O: Nginx proxuje `/api/` → `api:8080`, więc przeglądarka rozmawia z jednym originem — CORS i preflighty znikają z produkcji, na zewnątrz wystawiony jest jeden port, a API nie musi być publiczne. Do tego dev/prod parity: w dev identyczne proxy robi Vite (`server.proxy` → `localhost:5269`), więc kod aplikacji używa względnych ścieżek i niczego nie przełącza między środowiskami.

**P: Jak działa `VITE_API_BASE_URL` i jakie ma ograniczenie?**
O: `import.meta.env` jest wstrzykiwane w czasie **builda** — Vite zapieka wartość w bundlu, to nie jest runtime env jak w backendzie. Tu domyślnie pusty string (ścieżki względne + proxy), więc jeden obraz działa wszędzie; gdybym potrzebował różnych URL-i per środowisko z jednego obrazu, dałbym config wstrzykiwany w runtime (np. plik generowany na starcie kontenera).

---

## 7. Pytania „gwiazdkowe" (senior / dyskusja)

**P: Polling ściąga całą listę co 2 s, nawet gdy wszystko jest `completed`. Obroń to albo popraw.**
O: Świadoma decyzja (jest na to test: „always-poll") — nowe prompty może dodać inna karta/klient, więc lista nigdy nie jest „skończona", a warunkowe zatrzymywanie pollingu to nowa klasa bugów. Optymalizacje po kolei, gdy zaboli: dłuższy interwał z backoffem gdy brak aktywnych statusów, `If-None-Match`/ETag (304 zamiast pełnego body), delta-endpoint `?since=updatedAt`, na końcu SSE/websockety — każda podnosi złożoność, więc wchodzi dopiero za dowodem potrzeby.

**P: Nie widzę `React.memo` ani `useCallback`. Zaniedbanie?**
O: Decyzja. Lista ma dziesiątki elementów renderujących tanie DOM-owe węzły — re-render co tick jest niezauważalny, a memoizacja to koszt złożoności i sama nie jest darmowa (porównania propsów, utrzymanie stabilnych referencji). Reguła: najpierw pomiar (Profiler), potem memo. Gdyby lista urosła do tysięcy — najpierw wirtualizacja, nie memo.

**P: Co się stanie, gdy użytkownik kliknie „Wyślij" i natychmiast poleci `refetch`, a w locie wisi tick pollingu?**
O: Nic złego — `refetch` zmienia `trigger`, React robi cleanup starego efektu (abort in-flight żądania, clearTimeout) zanim uruchomi nowy. Konstrukcyjnie nie ma dwóch równoległych pętli ani race'a stary-response-nadpisuje-nowy, bo stary cykl jest martwy (flaga `cancelled`) zanim nowy wystartuje.

**P: Formularz robi refetch po sukcesie. Czemu nie optimistic update — dokleić prompty do listy lokalnie?**
O: POST zwraca tylko `ids` + status, a właścicielem listy jest hook pollingu — doklejanie lokalne tworzy drugi source of truth i ryzyko rozjazdu (kolejność z serwera to `CreatedAt`). Refetch po sukcesie daje spójność za cenę jednego żądania, a najdalej po 2 s polling i tak by to pokazał. Optimistic UI ma sens przy wolnych mutacjach i wymaganiu natychmiastowości — tu nie ma żadnego z tych warunków.

**P: Gdzie ten frontend najszybciej „urośnie z butów" i co byś wtedy zmienił?**
O: Trzy miejsca: (1) drugi widok/zasób → React Query zamiast ręcznego pollingu; (2) routing → React Router, `try_files` już na to gotowy; (3) rosnąca lista → paginacja na API (backend ma deterministyczny sort, czyli fundament) + wirtualizacja listy. Architektura tego nie blokuje: logika już siedzi w hookach, więc wymiana warstwy danych nie dotyka komponentów.

---

## Protip na pytanie „jak to powstało tak szybko?"

Nie mów „wkleiłem prompt do AI". Powiedz: pracowałem agentowo (Claude Code) z własnym procesem — zdefiniowałem w repo skille z konwencjami kodu (`.claude/skills/`), agentów o rolach architekt/implementator/krytyk/reviewer, i pełną dekompozycję techniczną w `doc/projects/`. AI odwaliło boilerplate, a decyzje architektoniczne — te, o które teraz pytacie — były moje i umiem każdą obronić. To zamienia „użył AI" z zarzutu w kompetencję.
