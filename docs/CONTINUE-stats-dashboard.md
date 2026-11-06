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
