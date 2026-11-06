# Etap 6 – Statystyki (feature/stats-dashboard) – KONTRAKT

Frontend powstaje PRZED backendem – ten kontrakt jest źródłem prawdy. JSON camelCase, enumy jako liczby, daty UTC ISO (`Z`), dni jako `YYYY-MM-DD` (UTC).

## Wyświetlenia ofert (nowe dane)
- Backend liczy wyświetlenia szczegółów oferty (`GET /api/jobs/{id}` przez nie-członka firmy i nie-admina), deduplikowane per (oferta, odwiedzający, dzień UTC) – odwiedzający = id użytkownika albo skrót (HMAC) IP+User-Agent dla anonimów; nie przechowujemy surowego IP. Agregaty dzienne `JobOfferDailyViews { jobOfferId, date, views }`.

## Parametry wspólne
- `from`, `to` (daty `YYYY-MM-DD`, włącznie; domyślnie ostatnie 30 dni; maks. zakres 366 dni → inaczej 400 `{message:"Date range cannot exceed 366 days"}`; `from > to` → 400 `{message:"Invalid date range"}`).

## `GET /api/admin/stats` [Admin]
```
{
  range: { from, to },
  totals: {
    users: { candidates, employers, admins },
    companies: { pending, approved, rejected },
    jobs: { draft, active, expired, closed },
    applications: { total, byStatus: [{ status, count }] }   // status = ApplicationStatus 0–6
  },
  series: [{ date, newApplications, newJobs, newUsers, jobViews }],   // każdy dzień zakresu, także zera
  funnel: { submitted, inReview, interview, offered, hired },          // aplikacje złożone w zakresie, które OSIĄGNĘŁY dany etap (wg historii)
  topJobs: [{ jobId, title, company, applications, views }],           // top 10 w zakresie po aplikacjach
  medianFirstResponseHours: number | null                              // mediana czasu od złożenia do pierwszej zmiany statusu przez rekrutera
}
```

## `GET /api/employer/stats` [Employer]
- Te same parametry; zakres danych: tylko oferty firmy pracodawcy.
```
{
  range, 
  totals: { jobs: { draft, active, expired, closed }, applications: { total, byStatus }, views },
  series: [{ date, newApplications, jobViews }],
  funnel: { submitted, inReview, interview, offered, hired },
  jobs: [{ jobId, title, status, isExpired, views, applications, conversionRate }],   // wszystkie oferty firmy; conversionRate = applications/views (0 gdy views = 0), posortowane po applications desc
  medianFirstResponseHours
}
```
- Firma nie-Approved – dane i tak zwracane (pracodawca widzi swoje szkice).

## `GET /api/employer/stats/export` i `GET /api/admin/stats/export` (te same parametry)
- CSV (UTF-8 z BOM, separator `;`, nagłówek po polsku) z tabelą `series` → `Content-Disposition: attachment; filename*=UTF-8''statystyki-YYYY-MM-DD_YYYY-MM-DD.csv`. Ochrona przed CSV injection (wartości zaczynające się od `=`, `+`, `-`, `@` poprzedzone `'`).

## Frontend
- Panel admina: nowa zakładka „Statystyki”; panel pracodawcy: nowa zakładka „Statystyki”. Oba komponenty lazy (nie w initial bundle), OnPush.
- Wybór zakresu: presety (7 dni, 30 dni, 90 dni, 12 miesięcy) + własny zakres (MatDateRangePicker); zakres w query params URL.
- Kafelki KPI (liczby z etykietami), wykres liniowy szeregu czasowego (aplikacje, wyświetlenia; admin także nowe oferty/użytkownicy), lejek rekrutacji (poziome słupki z liczbą i % względem `submitted`), tabela top ofert / ofert firmy (sortowalna), mediana czasu odpowiedzi („~X h” / „brak danych”).
- Wykresy: własne lekkie SVG (BEZ ciężkich bibliotek – CWV), responsywne (viewBox), z `role="img"` + `aria-label` z podsumowaniem i z dostępną alternatywą: przełącznik „Pokaż jako tabelę” (prawdziwa `<table>` z danymi); kolory spełniające kontrast 3:1 dla elementów graficznych, serie rozróżnialne nie tylko kolorem (styl linii/znaczniki), legenda tekstowa; tooltip dostępny z klawiatury (fokus na punktach) albo pominięty na rzecz tabeli.
- Stany: ładowanie (skeleton o stałej wysokości – brak CLS), błąd z „Spróbuj ponownie”, brak danych.
- Przycisk „Eksportuj CSV” (pobieranie blob jak przy załącznikach, `revokeObjectURL`).

## Stan prac

Frontend jest robiony PRZED backendem (równolegle z etapem 3 i aktualizacjami), w worktree `.worktrees/stats-dashboard`, od commita `a421eff` gałęzi `feature/cv-upload`. Backend powstanie później wg tego kontraktu (po merge'u aktualizacji zależności, etapu 3 i przeglądu jakości do `main`). Potem: testy, review, poprawki, dopasowanie do nowej wersji Angulara, merge.

## Frontend – stan

Zaimplementowane wg kontraktu (frontend/src/app):

- `models/stats.model.ts` – typy odpowiedzi admina i pracodawcy; `services/stats.service.ts` – `getAdminStats`, `getEmployerStats`, `exportCsv(mode, range)` (blob + `resolveDownloadFileName` + `saveBlobAsFile`, fallback nazwy `statystyki-<od>_<do>.csv`, błąd z blobu parsowany do JSON).
- `components/stats/` – `StatsDashboardComponent` (wspólny, `mode` = `admin` | `employer`) oraz podkomponenty OnPush: zakres dat (`stats-range-picker`), kafelki KPI, wykres liniowy SVG, lejek SVG, sortowalna tabela ofert. Zakładka „Statystyki” (ostatnia) w panelu admina i pracodawcy; zawartość w `@defer (on immediate)`, więc kod dashboardu i MatDatepicker jest w osobnym chunku, a nie w initial bundle.
- `utils/stats-range.ts` (presety, parsowanie/walidacja zakresu, daty po polsku), `utils/chart-scale.ts` (skala „nice”, ścieżki, indeksy etykiet, znaczniki), `utils/stats-view.ts` (modele widoku, opisy `aria-label`).
- Zakres w query params `from`/`to` (źródło prawdy to URL; zmiana zakresu = `router.navigate` z `replaceUrl`, domyślny zakres nie zapisuje się do URL). Niepoprawne parametry (zły format, `from > to`, > 366 dni) cicho wracają do domyślnych 30 dni.
- Dane ładowane przez `toObservable(...)` + `switchMap` (zmiana zakresu/ponowienie anuluje trwające żądanie), `takeUntilDestroyed`; brak timerów.
- Wykresy: `viewBox` (responsywne), `role="img"` z `aria-label` (zakres dat, sumy, dzień szczytu), osie z polskimi etykietami dat, legenda tekstowa będąca przyciskami (`aria-pressed`; skala Y dopasowuje się do widocznych serii), serie rozróżnialne kolorem + kreskowaniem + kształtem znacznika, przełącznik „Pokaż jako tabelę” (prawdziwa `<table>` z `<caption>` i `scope`).

### Rozstrzygnięcia niejasności kontraktu

- „Tooltip dostępny z klawiatury albo pominięty” – pominięty; zamiast niego tabela z dokładnymi wartościami.
- Dni statystyk są w UTC: presety liczone od dnia UTC (`toISOString`), a daty wybrane w kalendarzu bierzemy jako dzień kalendarzowy bez przesunięcia strefy.
- Jedna wspólna skala Y dla wszystkich serii (np. wyświetlenia vs nowi użytkownicy mają różne rzędy wielkości) – legenda pozwala ukryć serię, a skala dopasowuje się do pozostałych (ostatniej widocznej nie da się ukryć).
- Dla zakresu bez żadnej aktywności (same zera, `submitted = 0`) zamiast wykresów pokazujemy stan „Brak danych w wybranym zakresie”; kafelki KPI (stany całkowite) i tabela ofert pozostają widoczne.
- Mediana: „~X h” (jedno miejsce po przecinku poniżej 10 h), `null` -> „brak danych”.
- Tabela admina to `topJobs` (kolumny: oferta, firma, aplikacje, wyświetlenia), pracodawcy – `jobs` (status liczony z `status` + `isExpired`, konwersja = `conversionRate`); domyślne sortowanie po aplikacjach malejąco.
- Zakres własny > 366 dni jest odrzucany po stronie klienta (komunikat `role="alert"`), bez żądania; komunikaty 400 z API są tłumaczone (`Date range cannot exceed 366 days`, `Invalid date range`).
- Zakładka nie jest odzwierciedlona w URL (odświeżenie `/admin?from=..&to=..` otwiera pierwszą zakładkę); zakres w URL wspólny dla trybu admina i pracodawcy (osobne trasy).
- Po powrocie na zakładkę dane nie są przeładowywane automatycznie (zakładki mają `preserveContent`); dostępny przycisk „Odśwież”.

### Weryfikacja i ryzyka (frontend)

- `npx ng build` przechodzi bez błędów; paczka startowa 629,32 kB (ostrzeżenie budżetu 500 kB istniało już wcześniej). Analiza `--stats-json`: w chunkach startowych nie ma żadnego pliku z `components/stats`, `stats.service`, `http-errors`, MatDatepicker ani MatSlideToggle – dashboard jest osobnym leniwym chunkiem (`stats-dashboard-component`, ok. 61 kB surowo / 14 kB po kompresji). Różnica względem poprzednio zanotowanych 623,41 kB wynika z wersji zainstalowanych zależności w tym worktree (Angular 21.0.6, Material 21.0.5), nie z tych zmian.
- Po merge'u `main` (Angular 22) specy zostały uruchomione na kopii w kontenerze: cały zestaw przechodzi (wynik w raporcie końcowym); jedyna poprawka dotyczyła testu timerów dashboardu (Angular 22 planuje kolejne sprawdzenie zmian na najbliższą klatkę, więc test przewija klatkę i porównuje liczbę timerów z tą sprzed zniszczenia komponentu). Komponenty mają jawne `ChangeDetectionStrategy.OnPush` (ważne także w Angularze 22). `ng build --stats-json`: paczka startowa 600,92 kB (ostrzeżenie budżetu 500 kB istniało wcześniej na `main`); w chunkach startowych nie ma plików ze statystykami, MatDatepicker ani MatSlideToggle, dashboard to osobny chunk `stats-dashboard-component` (ok. 64 kB surowo / 14,7 kB po kompresji).

## Backend – stan

Zaimplementowane wg kontraktu (Backend/): `AdminStatsController` (`GET /api/admin/stats`, `/export`), `EmployerStatsController` (`GET /api/employer/stats`, `/export`) na wspólnej `StatsControllerBase`, `StatsService` (agregacje), `JobViewTracker` + `JobViewCleanupService` (wyświetlenia), `StatsRange`, `CsvFile`, DTO w `DTOs/StatsDto.cs`. Migracja `AddJobViewStatistics`. Opis dla użytkownika: sekcja „Statistics” w README.

### Rozstrzygnięcia niejasności kontraktu

- `totals` to stan bieżący / od początku (użytkownicy wg ról, firmy wg statusu, oferty wg stanu, aplikacje wg statusu – zawsze wszystkie statusy 0–6 z zerami, `views` pracodawcy od początku liczenia). Seria, lejek, mediana, `topJobs` i tabela `jobs` pracodawcy dotyczą zakresu – tak używa ich już frontend („Aplikacje (łącznie)” vs „… w zakresie”). Liczby `applications` i `views` w `topJobs` oraz `jobs` są z zakresu, więc `conversionRate` = aplikacje / wyświetlenia z tego samego okresu.
- Lejek jest narastający: etap „osiągnięty” = w historii aplikacji jest wpis ze statusem tego etapu albo wyższego etapu (InReview < Interview < Offered < Hired), więc zatrudniony bez wpisu „Rozmowa” liczy się do wszystkich etapów. Odrzucenie i wycofanie nie są etapami; `submitted` = wszystkie aplikacje złożone w zakresie (także wycofane).
- Mediana: czas od `AppliedAt` do pierwszego wpisu historii ze statusem innym niż Submitted i autorem innym niż kandydat (autor `null` po usuniętym koncie nie liczy się); sama notatka rekrutera nie jest odpowiedzią. Różnice w sekundach liczy SQL (`TIMESTAMPDIFF`), mediana pobiera tylko 1–2 środkowe wiersze (`COUNT` + `ORDER BY ... OFFSET/LIMIT`), wynik w godzinach z 2 miejscami. Bez takich aplikacji – `null`.
- `topJobs`: tylko oferty z jakąkolwiek aktywnością w zakresie (aplikacje albo wyświetlenia), sortowanie: aplikacje malejąco, wyświetlenia malejąco, id. `jobs` pracodawcy: wszystkie oferty firmy (górny limit 500), sortowanie jak wyżej.
- Zakres: `from`/`to` czytane jako teksty `YYYY-MM-DD` (zły format = `400` `Invalid date range`, jak `from > to`); samo `from` kończy się dziś, samo `to` zaczyna 29 dni wcześniej; dopuszczalne lata 2000–2100 (ochrona przed przepełnieniem). Pracodawca bez firmy: `403` `Employer account is not linked to a company` (jak w panelu ofert). Admin na endpointach pracodawcy i odwrotnie – `403` (role rozłączne).
- CSV zawiera tylko serię dzienną (nagłówki po polsku); każda komórka przechodzi przez `CsvFile.Escape` (ochrona przed CSV injection działa też na przyszłe kolumny tekstowe).
- Wyświetlenia: odwiedzający = id użytkownika albo IP + User-Agent (anonim); zapisywany jest tylko HMAC-SHA256 (16 bajtów) z kluczem wyprowadzonym przez HKDF z `JwtSettings:Secret` (osobny parametr `info`), więc skrótu nie da się odtworzyć bez sekretu (rotacja sekretu = jednorazowe podwójne liczenie). Zliczanie: `INSERT IGNORE` do `JobOfferViewers` + `INSERT ... ON DUPLICATE KEY UPDATE` w `JobOfferDailyViews` w jednej transakcji, parametryzowane (`ExecuteSqlInterpolated`). Wynik 0 wierszy z pierwszego zapytania = dziś już liczony. Błąd (także przerwane żądanie) jest tylko logowany jako ostrzeżenie. Wpisy deduplikacji starsze niż wczoraj usuwa co godzinę `JobViewCleanupService` (partie po 5000, `DELETE ... LIMIT`). Tabele są powiązane kaskadowo z ofertą, więc reset danych demo i usunięcie oferty czyszczą też wyświetlenia.
- Adres klienta za nginxem: dodano `UseForwardedHeaders` (tylko `X-Forwarded-For`, jedno ogniwo, zaufane wyłącznie sieci prywatne 10/8, 172.16/12, 192.168/16, fc00::/7) – bez tego wszyscy anonimowi odwiedzający w stacku Dockera mieliby adres kontenera nginx. Port backendu w `docker-compose.yml` jest wystawiony tylko na `127.0.0.1`.
- Dodano singleton `TimeProvider` (`TryAddSingleton(TimeProvider.System)`), używany przez statystyki i sprzątanie.

### Wydajność i indeksy

Agregacje w SQL: seria = `GROUP BY` po dniu osobnym zapytaniem na źródło (aplikacje, oferty, użytkownicy, wyświetlenia), luki wypełnia serwer; lejek = jedno zapytanie (`MAX(status)` etapów na aplikację, grupowane); sumy – zliczenia warunkowe. Admin: ok. 14 zapytań, pracodawca: ok. 9, żadne nie ładuje surowych encji. Nowe indeksy: `JobApplications.AppliedAt`, `JobOffers.CreatedAt`, `AspNetUsers.CreatedAt` (zakresy dat), `JobOfferViewers.Date` (sprzątanie); klucze główne tabel wyświetleń `(JobOfferId, Date)` i `(JobOfferId, Date, VisitorHash)`.

### Weryfikacja

- Testy backendu (zgodnie ze zmianą zakresu – główne ścieżki): `StatsRangeTests`, `CsvFileTests` (jednostkowe), `StatsTests` (każda metryka na kontrolowanych danych z zakresu 2024-05-01..2024-05-10: seria z zerami i granicami doby, sumy, lejek, mediana, tabele, top 10, kontrakt JSON, zakresy, autoryzacja, eksport CSV), `JobViewTrackingTests` (deduplikacja, równoległość, zarządzający nie są liczeni, szkice, błąd zliczania, sprzątanie, brak surowych danych), `AddJobViewStatisticsMigrationTests`. Dane testowe stawia `StatsFixture` (własna baza).
- Smoke test na żywo (Development, baza `JobBoardDb_stats`): wyświetlenia anonimowe / kandydaci / pracodawca / admin, aplikacja i zmiany statusów, oba endpointy, oba eksporty CSV (BOM, `;`, nagłówki pobierania), błędy zakresu i 403 – zgodnie z oczekiwaniami.

### Ryzyka i odstępstwa

- Wyświetlenia anonimowe to metryka popularności, nie rozliczenie: klient zmieniający User-Agent zawyża licznik, a anonimy z tym samym IP i User-Agentem (np. biuro za NAT) liczą się jako jeden.
- Brak limitera żądań na endpointach statystyk (zapytania są indeksowane, a zakres ograniczony do 366 dni); seeder nie tworzy wyświetleń, więc na świeżej bazie demo wykresy wyświetleń są puste do czasu ruchu.
- Migracja tworzy indeksy zwykłym `CREATE INDEX` – na dużej tabeli `JobApplications` trwa to dłużej.
