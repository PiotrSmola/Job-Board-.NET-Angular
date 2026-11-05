# Etap 2 – Pracodawcy i firmy: notatki do kontynuacji

Gałąź `feature/employers` (od `main` po merge etapu 1). Implementacja przerwana przez limit sesji – backend i frontend były robione równolegle wg poniższego kontraktu. Sekcje „Backend – stan” i „Frontend – stan” na końcu opisują, co zostało zrobione.

Plan dalszych kroków: dokończyć implementację → testy (backend + frontend) → review → poprawki → merge do `main`. Potem etapy 3–6: CV upload (B, magazyn plików zgodny z S3 – NIE oficjalne MinIO, np. SeaweedFS), konto użytkownika + e-maile (Mailpit) + zapisane oferty (E/F/G), taksonomia ofert (H), statystyki (I).

## Środowisko deweloperskie

Budowanie i testy wyłącznie w kontenerach `jobboard-dev-sdk` (.NET SDK 9, repo w `/repo`, dotnet-ef w `/root/.dotnet/tools`), `jobboard-dev-node` (Node 22, `/repo/frontend`), `jobboard-dev-mysql` (root/devpass) przez `docker exec`. Kontenerów `job-board-net-angular-*` (docker compose) nie ruszać. Po restarcie Dockera: `docker start jobboard-dev-mysql jobboard-dev-sdk jobboard-dev-node`.

Testy: `docker exec jobboard-dev-sdk bash -lc "cd /repo/Backend.Tests && dotnet test"`, `docker exec jobboard-dev-node npx ng test --watch=false`.

Commity: autor wyłącznie Piotr Smoła <piotrsmola2002@gmail.com>, bez Co-Authored-By i wzmianek o AI.


Cel: pracodawca rejestruje firmę, po zatwierdzeniu przez admina publikuje własne oferty i prowadzi rekrutację tylko w swoich ofertach. Admin moderuje firmy i nadal może wszystko.

### Model
- Nowa rola Identity: `Employer` (obok `Admin`, `User`). Rola dodawana w seederze (SeedRoles).
- `enum CompanyStatus { Pending = 0, Approved = 1, Rejected = 2 }`
- Encja `Company { Id, Name (max 200, UNIKALNA), Description (max 5000, nullable), Website (max 300, nullable, URL http/https), Location (max 200, nullable), Status, RejectionReason (max 1000, nullable), CreatedAt, ReviewedAt (nullable), ReviewedByUserId (nullable, FK SetNull) }`
- `ApplicationUser.CompanyId` (int?, FK do Company, OnDelete Restrict/SetNull – wybierz świadomie). Pracodawca należy do jednej firmy; firma może mieć wielu pracodawców (zespół – na razie tworzony tylko przy rejestracji/seedzie).
- `JobOffer.CompanyId` (int, wymagane po migracji, FK Restrict – nie można usunąć firmy z ofertami). Kolumna tekstowa `JobOffers.Company` zostaje USUNIĘTA po backfillu.
- Migracja (testowana na danych): dla każdej unikalnej (po trim) nazwy z `JobOffers.Company` utwórz `Company` (Status Approved, CreatedAt = najstarsza oferta), ustaw `JobOffers.CompanyId`, potem NOT NULL + FK + indeks, usuń starą kolumnę. Down odwraca (przywraca kolumnę z nazwą firmy).

### DTO (JSON camelCase, enumy jako liczby)
- `JobOfferDto` += `companyId`; `company` (string) ZOSTAJE i zawiera nazwę firmy (zgodność wsteczna).
- `JobApplicationDto`: `company` dalej nazwa firmy; += `companyId`.
- `CompanyDto { id, name, description, website, location, status, rejectionReason, createdAt, reviewedAt, activeJobsCount }` – `rejectionReason`/`reviewedAt`/`status` wypełniane tylko dla admina i członków firmy, publicznie null/pominięte (publiczny = tylko Approved).
- `UserDto` (login/register) += `role` – NIE; zamiast tego frontend czyta role z JWT jak dotąd (claim role). JWT zawiera rolę `Employer`.
- Formularz oferty: `CreateJobOfferDto`/`UpdateJobOfferDto`: pole tekstowe `company` USUNIĘTE, zamiast niego `companyId` (int) – wymagane dla admina, IGNOROWANE dla pracodawcy (zawsze jego firma).

### Endpointy
### Auth
- `POST /api/auth/register-employer` body `{ email, password, firstName, lastName, companyName, companyDescription?, website?, location? }` → 200 `UserDto` (token z rolą Employer). Firma tworzona jako Pending. Zajęta nazwa firmy (case-insensitive) → 400 `{message:"Company name is already taken"}`; zajęty email → 400 jak przy zwykłej rejestracji. Rejestracja w jednej transakcji (user + firma).
- `POST /api/auth/register` bez zmian (rola User).

### Publiczne firmy
- `GET /api/companies` – tylko Approved; paginacja (`pageNumber`, `pageSize` ≤50, `searchTerm` po nazwie/lokalizacji, `orderBy`: `nameAsc` (domyślnie) | `nameDesc` | `jobsDesc`), nagłówek `Pagination`. `activeJobsCount` = liczba ofert widocznych publicznie.
- `GET /api/companies/{id}` – Approved → 200 dla każdego; Pending/Rejected → 200 tylko dla admina i członków firmy, inaczej 404.
- `GET /api/jobs` (publiczne) += filtr `companyId`. Oferty firm nie-Approved NIE są widoczne publicznie (warunek widoczności: oferta widoczna wg etapu 1 ORAZ firma Approved). Tak samo `GET /api/jobs/{id}` dla nie-admina/nie-członka i aplikowanie (→ 404).
- Wyszukiwanie `searchTerm` i sortowanie `companyAsc/companyDesc` działają po `Company.Name`.

### Pracodawca (rola Employer)
- `GET /api/employer/company` → `CompanyDto` własnej firmy (pełny, ze statusem i rejectionReason).
- `PUT /api/employer/company` body `{ name, description?, website?, location? }` → 200 `CompanyDto`. Jeśli firma Rejected → po edycji wraca do Pending (ponowne zgłoszenie, RejectionReason czyszczony). Zmiana nazwy na zajętą → 400.
- Oferty: `GET /api/jobs/manage` – Employer widzi WYŁĄCZNIE oferty swojej firmy (Admin – wszystkie + filtr `companyId`). `POST /api/jobs`, `PUT /api/jobs/{id}`, `PATCH /api/jobs/{id}/status`, `DELETE /api/jobs/{id}` – dozwolone dla Admin oraz Employer będącego członkiem firmy oferty; cudza oferta → 404 (nie ujawniamy). Employer z firmą nie-Approved: może tworzyć/edytować TYLKO szkice (Draft); próba ustawienia Active → 403 `{message:"Company must be approved before publishing offers"}`.
- Aplikacje: `GET /api/jobapplications/all` – Employer widzi tylko aplikacje do ofert swojej firmy (Admin – wszystkie). `GET /api/jobapplications/job/{jobId}`, `GET /api/jobapplications/{id}`, `PUT /api/jobapplications/{id}/status` – Admin lub Employer firmy oferty (cudze → 404). `DELETE /api/jobapplications/{id}` – tylko Admin. `changedBy` w historii widoczne dla Admin i Employer firmy.
- Employer (i Admin) nie może aplikować na oferty: `POST /api/jobapplications` → 403.

### Admin – moderacja firm
- `GET /api/admin/companies` – wszystkie firmy, paginacja + `searchTerm`, `status` (0/1/2, opcjonalny), `orderBy` (`newest` domyślnie | `oldest` | `nameAsc` | `nameDesc`); DTO += `membersCount`.
- `POST /api/admin/companies` body `{ name, description?, website?, location? }` → 201, firma Approved od razu.
- `PUT /api/admin/companies/{id}` → edycja danych.
- `POST /api/admin/companies/{id}/approve` → 200 `CompanyDto` (Status Approved, ReviewedAt/By).
- `POST /api/admin/companies/{id}/reject` body `{ reason }` (wymagany, ≤1000) → 200 `CompanyDto`.
- `DELETE /api/admin/companies/{id}` → 204; gdy firma ma oferty lub członków → 400 `{message:"Company has job offers or members"}`.

### Seeder (dev/demo)
- Firmy tworzone z nazw seedowych ofert (Approved); oferty seedowe wskazują `CompanyId`.
- Konta demo: pracodawca zatwierdzonej firmy `rekruter@techcorp.pl` / `Employer123!` (firma = jedna z seedowych, np. ta z największą liczbą ofert – udokumentuj którą), pracodawca firmy oczekującej `rekruter@nowafirma.pl` / `Employer123!` (firma Pending „Nowa Firma Sp. z o.o.” bez ofert). RefreshSeed odtwarza to samo.

### Frontend
- Rejestracja: przełącznik „Szukam pracy” / „Rekrutuję”; tryb pracodawcy dodaje pola firmy i woła `register-employer`.
- `AuthService`: `isEmployer()` (rola Employer z JWT). Guard `employerGuard` (z returnUrl jak inne).
- Navbar: Employer → „Panel pracodawcy” (`/employer`); nie widzi „Moje aplikacje”.
- `/employer` – panel z zakładkami: „Oferty” (reużycie zakładki ofert admina w trybie pracodawcy: bez kolumny/wyboru firmy, `/api/jobs/manage`), „Aplikacje” (reużycie zakładki aplikacji admina, bez usuwania), „Profil firmy” (formularz + baner statusu: Oczekuje na weryfikację / Odrzucona: powód + „Wyślij ponownie” / Zatwierdzona). Gdy firma nie-Approved: w dialogu oferty status ograniczony do „Szkic” + informacja dlaczego.
- Panel admina: nowa zakładka „Firmy” (tabela z paginacją, filtr statusu, wyszukiwanie, akcje: Zatwierdź, Odrzuć (dialog z powodem), Edytuj/Dodaj (dialog), Usuń). Zakładka ofert: kolumna firmy + filtr firmy (autocomplete po `/api/admin/companies` lub `/api/companies`); dialog oferty: wybór firmy (autocomplete po zatwierdzonych firmach) zamiast pola tekstowego.
- Publiczne: `/companies` (lista firm z wyszukiwaniem i paginacją), `/companies/:id` (profil: opis, strona www, lokalizacja, lista aktywnych ofert przez `/api/jobs?companyId=`). Nazwa firmy w liście ofert i szczegółach oferty linkuje do profilu. Link „Firmy” w navbarze.
- Szczegóły oferty: Employer/Admin nie widzą przycisku aplikowania.
- Budżety stylów komponentów: ≤ 4 kB ostrzeżenie (nie dokładaj nowych ostrzeżeń), nie podnoś limitów.


## Frontend – stan

### Zrobione (commity eb67352, 4da6600, 1e748b1, f31f7d3, 2a52a01)
- Modele i serwisy: `models/company.model.ts`, `models/company-params.model.ts`, `models/panel-mode.model.ts`; `services/company.service.ts` (`/api/companies`, `/api/employer/company`), metody firm w `services/admin.service.ts` (`/api/admin/companies` + approve/reject/delete); `JobOffer`/`JobApplication` += `companyId`, `CreateJobOffer.companyId` (zamiast `company`), `JobParams.companyId`.
- Auth: `AuthService.isEmployer()` / `registerEmployer()`, `guards/employer.guard.ts`; rejestracja z przełącznikiem „Szukam pracy” / „Rekrutuję” (`?type=employer`), pracodawca po rejestracji → `/employer`.
- Zakładki ofert i aplikacji z wejściem `mode: 'admin' | 'employer'` (bez duplikacji): admin – kolumna i filtr firmy (autocomplete), employer – tylko własna firma, bez usuwania aplikacji, bez publikacji przy firmie niezatwierdzonej.
- `job-dialog`: `JobDialogData { job, mode, company }`, wybór firmy autocomplete (`components/shared/company-autocomplete.component.ts`, tylko zatwierdzone), dla niezatwierdzonej firmy pracodawcy status ograniczony do „Szkic” + informacja.
- Panel admina: zakładka „Firmy” (`admin-companies-tab`, `company-dialog`, `reject-company-dialog`).
- Trasy (leniwe): `/employer` (panel + `employer-company-tab` z banerem statusu i „Wyślij ponownie”), `/companies`, `/companies/:id`.
- Navbar: „Firmy”, „Panel pracodawcy”; employer nie widzi „Moje Aplikacje”. Nazwa firmy linkuje do profilu (lista ofert, szczegóły). Employer/Admin nie widzą aplikowania.
- `getApiErrorMessage`: tłumaczenia „Company name is already taken”, „Company must be approved before publishing offers”, „Company has job offers or members”, „Email is already taken”, „Company not found”; interceptor pokazuje przetłumaczony powód 403 (`utils/forbidden-messages.ts`).

### Wyniki
- `npx ng test --watch=false`: 25 plików, 573 testy zielone.
- `npx ng build`: bez błędów; tylko istniejące ostrzeżenia budżetu (initial 623,05 kB – wzrost o ~2,4 kB z tras/guarda/navbara; job-list.scss 5,13 kB).

### Brakuje
- Specy komponentów: `admin-companies-tab`, `company-dialog`, `reject-company-dialog`, `employer-panel`, `employer-company-tab`, `company-list`, `company-detail`, `company-autocomplete`, `register` (tryb pracodawcy).
- Weryfikacja na działającym API (format błędów `register-employer`, 404 dla niepublicznej firmy, `status` w `/api/employer/company`).
- Opcjonalnie: po logowaniu pracodawcy bez returnUrl → `/employer` (dziś `/jobs`, wymaga zmiany speca logowania); `/my-applications` dostępne dla pracodawcy z ręcznego URL (chroni tylko `authGuard`).

### Niejasności kontraktu – rozstrzygnięcia
- `CompanyId` w formularzu oferty ma w API `[Range(1,…)] int?` – pracodawca wysyła id swojej firmy albo `null` (nigdy 0).
- Niezatwierdzona firma: opcje statusu = „Szkic” + bieżący status edytowanej oferty; nowa oferta domyślnie szkic; w tabeli ukryte „Opublikuj”/„Wznów”.
- Filtr firm w zakładce ofert admina – `/api/admin/companies` (wszystkie statusy); dialog oferty – `/api/companies` (tylko zatwierdzone).
- Edycja zatwierdzonej firmy przez pracodawcę nie zmienia statusu (kontrakt mówi tylko o odrzuconej); odrzuconą można wysłać ponownie bez zmian.

### Kolejne kroki
1. Smoke test z backendem (rejestracja pracodawcy, moderacja, publikacja po zatwierdzeniu).
2. Testy komponentów z listy „Brakuje”.
