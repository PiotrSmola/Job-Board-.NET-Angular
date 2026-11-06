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

## Frontend – stan

Frontend etapu 5 jest zaimplementowany wg kontraktu (bez backendu – testy na stubach i `HttpTestingController`). Wszystkie nowe komponenty są `OnPush` i ładowane leniwie (lista ofert, szczegóły i panel admina były już leniwe; nic nie trafiło do initial bundle).

### Struktura
- Modele: `models/job-offer.model.ts` (`SeniorityLevel`, `ContractTypes` [flagi], `Currency`, `SalaryPeriod`, nowe pola `JobOffer`/`CreateJobOffer`), `models/taxonomy.model.ts` (`Category`, `SaveCategory`, `SkillSuggestion`, `JobFacets`), `models/job-params.model.ts` (`JobParams`: `workTypes[]` zamiast `workType`, `category` [slug], `seniority[]`, `contractTypes` [maska], `skills[]`, `currency`).
- Serwisy: `CategoryService` (`GET /api/categories`), `SkillService` (`GET /api/skills`, limit 1–20), `JobService.getFacets()` (`GET /api/jobs/facets`), `AdminService.create/update/deleteCategory`.
- Utils: `utils/job-labels.ts` (polskie etykiety, flagi, `formatSalaryRange` przez `Intl.NumberFormat('pl-PL')`, liczba mnoga „oferta/oferty/ofert”, `slugify`), `utils/job-query.ts` (jedna serializacja filtrów dla API i adresu URL + bezpieczny odczyt z URL), `utils/active-filters.ts` (chipy aktywnych filtrów).
- Komponenty: `job-list/job-filters` (panel filtrów), `job-list` (przepisana: OnPush + sygnały, URL jako źródło prawdy), `shared/removable-chips`, `shared/skills-input` (autocomplete + chipy; `allowCreate` w dialogu oferty), `shared/job-taxonomy` (tagi + umiejętności na karcie/szczegółach), `admin-panel/admin-categories-tab` + `admin-panel/category-dialog` (zakładka „Kategorie”).
- Zmienione: karta oferty, szczegóły oferty (kategoria jako link `/jobs?category=slug`, poziom, typy umów, umiejętności), dialog oferty (nowe pola), profil firmy (format widełek), `admin-jobs-tab` (`workTypes`), `http-errors` (tłumaczenie komunikatów kategorii).

### Synchronizacja z URL
Adres jest źródłem prawdy: lista subskrybuje `ActivatedRoute.queryParamMap` → `JobParams` → `switchMap` na ofertach i (osobno, tylko gdy zmienią się filtry) na facetach. Zmiany w panelu wywołują `router.navigate([], { queryParams, replaceUrl })`; checkboxy/radio/select/paginacja dodają wpis do historii (przycisk „wstecz” działa), pisanie w polach tekstowych/płacy (debounce 500/800 ms) używa `replaceUrl: true`. Nazwy parametrów URL = nazwy parametrów API (`searchTerm`, `location`, `category`, `seniority=1,2`, `contractTypes=<maska>`, `workType=0,1`, `skills=A,B`, `currency`, `minSalary`, `maxSalary`, `companyId`) + `page`, `pageSize`, `orderBy` (tylko gdy ≠ domyślne). Wartości z URL są walidowane (nieznane ignorowane, listy deduplikowane, umiejętności ≤ 15 po ≤ 50 znaków, slug `[a-z0-9-]`, `pageSize` ∈ {5,10,20,50}).

### Niejasności kontraktu i rozstrzygnięcia
1. **Przecinek w nazwie umiejętności.** Listy (`skills`, `seniority`, `workType`) są rozdzielane przecinkiem, więc nazwa z przecinkiem zepsułaby filtr. Frontend nie pozwala dodać umiejętności z przecinkiem (dialog oferty, filtry, URL). Backend powinien odrzucać/normalizować przecinki w nazwach umiejętności (albo kontrakt musi przewidzieć escapowanie).
2. **`category` vs `categoryId`.** Frontend wysyła tylko `category` (slug) – to jedyny identyfikator w linku z szczegółów oferty i w URL listy.
3. **`workType`.** Zawsze lista po przecinku (`workType=0` dla pojedynczej wartości); pole `JobParams.workType` zastąpiono `workTypes[]` (także w `ManageJobParams` i panelu admina).
4. **Waluta `PLN = 0`.** Filtr `currency=0` jest wysyłany (null = brak filtra); `minSalary`/`maxSalary` = 0 traktowane jak brak (jak dotąd).
5. **Facety `/api/jobs/facets`.** Wysyłane są tylko filtry (bez `pageNumber`/`pageSize`/`orderBy`). Brak wartości w odpowiedzi = 0 ofert (backend może pomijać zerowe liczniki). `categories[].count` dopasowywane po `id`; lista kategorii pochodzi z `/api/categories` (nie z facet), więc kategorie z licznikiem 0 są widoczne. Liczba `total` z facet nie jest używana (liczba wyników = nagłówek `Pagination` z `/api/jobs`).
6. **`activeJobsCount` w kategorii** to licznik aktywnych ofert (jak w kontrakcie); w zakładce admina podpisany „Aktywne oferty”.
7. **Dialog oferty.** `categoryId`/`seniority` = `null` oznacza „brak”; `contractTypes` = 0 gdy nic nie zaznaczono; `skills` bez duplikatów (bez wielkości liter), max 15, 1–50 znaków. Prefiks `$` przy polach płacy usunięto (sufiks to wybrana waluta).
8. **Nazwa umiejętności w filtrze** musi pochodzić z podpowiedzi API (Enter bez dopasowania pokazuje komunikat); w dialogu oferty można tworzyć nowe (Enter lub przecinek).
9. **Komunikaty API** `Category name or slug is already taken` / `Category not found` są tłumaczone w `utils/http-errors.ts` (`Category not found` jest założeniem – kontrakt go nie wymienia).
10. **Slug kategorii** ma wzorzec `^[a-z0-9-]+$` (≤ 100 znaków), generowany z nazwy do czasu ręcznej edycji.
11. **„Powrót do listy”** w szczegółach oferty nadal nawiguje do `/jobs` (bez filtrów) – przycisk „wstecz” przeglądarki zachowuje filtry; nie zmieniano tego zachowania.
12. **`useGrouping: 'always'`** w `Intl.NumberFormat` – bez niego `pl-PL` nie grupuje liczb czterocyfrowych („8000” zamiast „8 000”); starsze przeglądarki bez tej opcji pokażą „8000”.

### Wyniki i ryzyka
- Testy: `ng test --watch=false` – 53 pliki, 1431 testów zielone. Build: `ng build` bez błędów; initial 624,87 kB (było ~623 kB – różnica to etykiety/serwisy w `http-errors`/`admin.service`), `job-list.component.scss` 4,30 kB (było 5,13 kB; ostrzeżenie budżetu 4 kB istniało wcześniej, nie pojawiło się żadne nowe).
- Uruchamianie testów w worktree: `node_modules` na bind-mouncie są bardzo wolne (workery vitest kończą się „Timeout waiting for worker to respond”). Skuteczny obejście: skopiować `node_modules` i `src` do systemu plików kontenera (np. `/tmp/fe-tax`) i uruchomić tam `ng test` (całość ~20 s).
- Ryzyka: (1) backend jeszcze nie istnieje – kształt odpowiedzi `/api/jobs/facets` i `/api/skills` oparty wyłącznie na kontrakcie; (2) filtr umiejętności nie obsługuje nazw z przecinkiem (patrz punkt 1); (3) `useGrouping: 'always'` wymaga nowszych przeglądarek dla spacji w „8 000”; (4) panel filtrów na wąskich ekranach jest rozwijany przyciskiem „Filtry” (nie dialogiem).

## Backend – stan

Backend etapu 5 jest zaimplementowany wg kontraktu i frontendowych rozstrzygnięć (gałąź `feature/job-taxonomy`, po merge'u `main` z Angularem 22). Migracja: `20261106160558_AddJobTaxonomy`.

### Struktura
- Model: `Category`, `Skill` (wiele-do-wielu z `JobOffer` przez tabelę `JobOfferSkills`, kaskady po obu stronach), enumy `SeniorityLevel`, `ContractTypes` ([Flags]), `Currency`, `SalaryPeriod`; `JobOffer` += `CategoryId?` (FK `SET NULL`), `Seniority?`, `ContractTypes`, `SalaryCurrency`, `SalaryPeriod` (kolumny NOT NULL z domyślnym 0 w migracji – istniejące oferty: PLN, miesięcznie, bez typów umów).
- Indeksy: unikalne `Categories.Name`, `Categories.Slug`, `Skills.NormalizedName`; `JobOffers.CategoryId` (FK), `Seniority`, `SalaryCurrency` (maska typów umów nie jest indeksowalna). `Skills.NormalizedName` ma kolację `utf8mb4_bin` i zawiera nazwę zapisaną małymi literami – dopasowanie jest dokładne i takie samo w bazie i w kodzie.
- Filtry: `JobParams` trzyma surowe wartości zapytania (listy po przecinku jako tekst), `JobFilterParser` waliduje je (`IValidatableObject` → 400) i buduje `JobFilter`; `JobRepository.ApplyFilter` stosuje go jako `IQueryable` (SQL) – ten sam kod dla `/api/jobs`, `/api/jobs/manage` i faset.
- Endpointy: `GET /api/categories` (+ `GET /api/categories/{id}` dla nagłówka `Location` po POST), `POST/PUT/DELETE /api/admin/categories` (tylko admin), `GET /api/skills`, `GET /api/jobs/facets`.
- Seeder: 8 kategorii (tworzone, gdy tabela jest pusta), taksonomia ofert demo wg tytułu; w `RefreshSeed` kategorie i umiejętności są usuwane i odtwarzane; dla bazy sprzed etapu 5 oferty demo bez żadnej taksonomii są uzupełniane (tylko gdy kategorie dopiero powstały).

### Rozstrzygnięcia
1. **Przecinek w nazwie umiejętności** → `400` (pole `Skills`), tak samo nazwa pusta po normalizacji (białe/niewidoczne znaki), dłuższa niż 50 znaków i więcej niż 15 umiejętności. Nazwy są normalizowane (przycięte, zwinięte odstępy, bez znaków niewidocznych), duplikaty bez wielkości liter scalane, nazwa wyświetlana = pierwsza użyta w bazie.
2. **Limity filtrów** (zgodnie z poleceniem): `skills` ≤ 10 nazw po ≤ 50 znaków, `category` ≤ 100 (wzorzec `[a-z0-9-]`), `seniority`/`workType` ≤ 50 znaków i tylko znane wartości, `contractTypes` 0–31, `currency` znana, `searchTerm`/`location` ≤ 200 → `400`. **Frontend dostosowany**: filtr umiejętności ma limit 10 (`MAX_FILTER_SKILLS`), oferta nadal 15.
3. **`category` i `categoryId`** naraz = oba warunki (AND); frontend wysyła tylko `category`.
4. **`workType`** przyjmuje listę (`0,1`), pojedynczą liczbę i nazwę (`Onsite`) – zgodność wsteczna z testami; `currency=0` jest realnym filtrem.
5. **Facety**: sześć zapytań agregujących w SQL (łącznie, kategorie, poziomy, tryby pracy, bity umów jednym zapytaniem z licznikami warunkowymi, top 20 umiejętności); licznik fasety pomija jej własny filtr i stosuje pozostałe, wartości z licznikiem 0 są pomijane, kategorie posortowane malejąco po liczniku. Liczą wyłącznie oferty widoczne publicznie (aktywne, niewygasłe, firma zatwierdzona) – tak samo jak `activeJobsCount` kategorii i `jobsCount` umiejętności. Faseta umiejętności pomija filtr `skills` (nie tylko jedną nazwę).
6. **`GET /api/skills`**: podpowiedzi tylko z ofert widocznych publicznie (umiejętność użyta wyłącznie w szkicach nie jest podpowiadana); fraza = fragment nazwy, prefiksy przed fragmentami, potem popularność i nazwa; `limit` 1–20 (poza zakresem `400`), `search` ≤ 50 znaków.
7. **PUT oferty zastępuje całą taksonomię** (brak pola = wartość domyślna / brak kategorii / brak umiejętności); szybka zmiana statusu (`PATCH`) zachowuje taksonomię. Nieistniejąca kategoria w ofercie → `400 {"message":"Category not found"}`; `GET/PUT/DELETE` nieistniejącej kategorii w panelu admina → `404` z tym samym komunikatem. Zajęta nazwa/slug (bez wielkości liter i akcentów, także przy wyścigu) → `400 Category name or slug is already taken`.
8. **Uprawnienia**: kategorie wyłącznie admin; pracodawca ustawia taksonomię tylko w ofertach własnej firmy (istniejąca kontrola `ICurrentUserAccess`, cudza oferta → 404).
9. **Umiejętności tworzone przed zapisem oferty** (osobne zatwierdzenie); wyścig o tę samą nową nazwę rozstrzyga unikalny indeks i ponowienie dopasowania (do 3 prób). Porzucone umiejętności (po usunięciu ofert) zostają w słowniku, ale nie są podpowiadane.
10. **DTO aplikacji** (`JobApplicationDto.jobOffer`) nie ładuje kategorii ani umiejętności (puste/null) – frontend ich tam nie używa.

### Wyniki i ryzyka
- Testy etapu 5 (filtry, facety, umiejętności, kategorie, migracja): 75 testów zielonych (`--filter JobTaxonomy|CategoriesTests|AddJobTaxonomyMigration`); wcześniejsze klasy dotykające ofert (JobsQuery, JobsCrud, EmployerJobs, AdminSeedRefresh) zielone; pełny zestaw backendu nie był uruchamiany na koniec (decyzja o skróceniu zakresu). Frontend: pełne `ng test` 1476 testów zielone, `ng build` bez nowych ostrzeżeń (budżet initial i `job-list.component.scss` ostrzegały już wcześniej). Smoke test na żywo (baza `JobBoardDb_taxonomy`): facety z filtrami, złożony filtr, CRUD kategorii.
- Ryzyka: (1) wyścig usunięcia kategorii z zapisem oferty wskazującej tę kategorię daje FK-violation (500) zamiast 400 – okno milisekund; (2) brak limitu zapytań publicznych list (robi to osobna gałąź); (3) przy dużej liczbie ofert warto rozważyć zdenormalizowane liczniki – obecnie wszystko liczone w SQL na żądanie.
