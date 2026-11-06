# Etap 5 – Taksonomia i filtry ofert (feature/job-taxonomy) – KONTRAKT

Frontend powstaje PRZED backendem – ten kontrakt jest źródłem prawdy. JSON camelCase, enumy jako liczby.

## Model
- `Category { id, name (≤100, unikalna), slug (unikalny, a-z0-9-), sortOrder }` – zarządzane przez admina.
- `enum SeniorityLevel { Intern = 0, Junior = 1, Mid = 2, Senior = 3, Lead = 4, Manager = 5 }`.
- `[Flags] enum ContractTypes { None = 0, EmploymentContract = 1, B2B = 2, MandateContract = 4, SpecificTaskContract = 8, Internship = 16 }`.
- `enum Currency { PLN = 0, EUR = 1, USD = 2, GBP = 3 }`, `enum SalaryPeriod { Month = 0, Hour = 1, Year = 2 }`.
- `Skill { id, name (≤50, wyświetlana), normalizedName (unikalna) }`; oferta ma 0–15 umiejętności (relacja wiele-do-wielu).
- `JobOffer` += `categoryId?`, `seniority?`, `contractTypes` (int flags, domyślnie 0), `salaryCurrency` (domyślnie PLN), `salaryPeriod` (domyślnie Month), `skills`.

## DTO
- `JobOfferDto` += `categoryId`, `categoryName`, `categorySlug`, `seniority`, `contractTypes`, `salaryCurrency`, `salaryPeriod`, `skills: string[]`.
- Formularz oferty (POST/PUT) += `categoryId?`, `seniority?`, `contractTypes`, `salaryCurrency`, `salaryPeriod`, `skills: string[]` (0–15, każda 1–50 znaków po trim; nowe umiejętności tworzone automatycznie, dopasowanie bez wielkości liter).

## Endpointy
- `GET /api/categories` (publiczny) → `[{ id, name, slug, sortOrder, activeJobsCount }]` posortowane po `sortOrder`, `name`.
- Admin: `POST /api/admin/categories` `{ name, slug, sortOrder }` → 201; `PUT /api/admin/categories/{id}` → 200; `DELETE /api/admin/categories/{id}` → 204 (oferty tracą kategorię → null). Zajęta nazwa/slug → 400 `{message:"Category name or slug is already taken"}`.
- `GET /api/skills?search=&limit=` (publiczny, limit ≤ 20, domyślnie 10) → `[{ name, jobsCount }]` – podpowiedzi (prefiks lub fragment, bez wielkości liter), sortowane po popularności.
- `GET /api/jobs` i `GET /api/jobs/manage` – nowe filtry (wszystkie opcjonalne, łączone AND):
  - `category` (slug) lub `categoryId`,
  - `seniority` – lista po przecinku (`1,2`) = OR w obrębie,
  - `contractTypes` – maska bitowa; oferta pasuje, gdy ma KTÓRYKOLWIEK z zaznaczonych typów,
  - `workType` – teraz także lista po przecinku (zgodność wsteczna z pojedynczą liczbą),
  - `skills` – lista po przecinku nazw; oferta musi mieć WSZYSTKIE,
  - `currency` – filtr waluty; `minSalary`/`maxSalary` bez zmian (w walucie oferty, bez przeliczania).
- `GET /api/jobs/facets` (publiczny, te same parametry filtrów co `/api/jobs`, bez paginacji) → `{ total, categories: [{ id, slug, name, count }], seniority: [{ value, count }], contractTypes: [{ value (pojedynczy bit), count }], workTypes: [{ value, count }], skills: [{ name, count }] (top 20) }`. Liczniki fasety X liczone z pominięciem filtra X (aby można było rozszerzać wybór).

## Seeder
- Kategorie: Programowanie, DevOps i chmura, Testowanie, Analiza danych, Design UX/UI, Zarządzanie projektami, Bezpieczeństwo IT, Wsparcie IT. Oferty seedowe dostają kategorie, poziomy, typy umów, waluty, umiejętności (realistyczne).

## Frontend
- Lista ofert: panel filtrów (na mobile jako rozwijany panel/dialog) z sekcjami: kategoria, poziom (checkboxy), typ umowy (checkboxy), tryb pracy (checkboxy), umiejętności (autocomplete z `/api/skills`, chipy), waluta + widełki; przy opcjach liczniki z `/api/jobs/facets`; „Wyczyść filtry”; aktywne filtry jako usuwalne chipy nad listą.
- Synchronizacja filtrów z query params URL (udostępnialne linki, przycisk wstecz działa); debounce zmian; anulowanie poprzednich żądań (`switchMap`).
- Karta oferty: kategoria, poziom, typy umów, widełki z walutą i okresem („15 000–20 000 PLN / mies.”, „120–150 PLN / h”), do 5 umiejętności jako chipy + „+N”.
- Szczegóły oferty: pełna lista umiejętności, kategoria (link do listy z filtrem), poziom, typy umów.
- Dialog oferty (admin/pracodawca): nowe pola – kategoria (select), poziom, typy umów (checkboxy), waluta, okres, umiejętności (autocomplete + tworzenie nowych, chipy, limit 15).
- Panel admina: zakładka „Kategorie” (CRUD, sortOrder, licznik ofert).
- Polskie etykiety: Stażysta/Junior/Mid/Senior/Lead/Manager; Umowa o pracę/B2B/Umowa zlecenie/Umowa o dzieło/Staż; mies./h/rok.
- WCAG: grupy checkboxów w `fieldset`/`legend`, liczniki w etykiecie dostępnej („Junior, 12 ofert”), zmiana wyników ogłaszana (`aria-live="polite"`: „Znaleziono N ofert”), filtry obsługiwane klawiaturą; CWV: liczba wyników i lista z zarezerwowaną wysokością (skeleton), bez przeskoków przy zmianie filtrów.

## Stan prac

Frontend jest robiony PRZED backendem (równolegle z etapem 3 i aktualizacjami), w worktree `.worktrees/job-taxonomy`, od commita `a421eff` gałęzi `feature/cv-upload`. Backend powstanie później wg tego kontraktu (po merge'u aktualizacji zależności, etapu 3 i przeglądu jakości do `main`). Potem: testy, review, poprawki, dopasowanie do nowej wersji Angulara, merge.
