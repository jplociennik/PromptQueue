# Pytania rekrutacyjne (fullstack) — frontend PromptQueue

Pytania, jakie można dostać na rozmowie na podstawie kodu w `frontend/`, z krótkimi odpowiedziami „jak na interview". Wszystkie odnoszą się do rzeczy, które realnie są w tym repo.

---

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
