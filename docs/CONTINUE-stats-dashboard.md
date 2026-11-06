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
- Specy napisane (utils, serwis, 5 komponentów, zakładki w obu panelach), ale NIE zostały jeszcze uruchomione: kontener `jobboard-dev-node` miał wyczerpany swap przez osierocone procesy `ng test`/vitest z wielu sesji i workery nie startowały (`Timeout waiting for worker to respond`). Kompilacja specyfikacji (`ng test` – etap budowania) przechodzi. Po zwolnieniu zasobów uruchomić pełny `npx ng test --watch=false`.
