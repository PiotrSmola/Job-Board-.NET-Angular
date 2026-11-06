# Job-Board-.NET-Angular

## Uruchomienie w Dockerze

Wymagany Docker Desktop (Docker Compose v2).

```powershell
.\docker-up.ps1
```

Skrypt buduje obrazy i uruchamia cztery kontenery: MySQL, magazyn plików załączników aplikacji (SeaweedFS z API zgodnym z S3, bez publikowanych portów - dostępny tylko dla backendu w sieci compose), backend (ASP.NET Core) i frontend (Angular serwowany przez nginx, który przekazuje `/api` do backendu). Domyślne porty to 4200 (frontend), 5285 (backend / Swagger) i 3306 (MySQL) - jeśli któryś jest zajęty, wybierany jest kolejny wolny. Wybrane porty zapisywane są do `.env` i wypisywane na koniec.

Preferowane porty można zmienić parametrami:

```powershell
.\docker-up.ps1 -FrontendPort 8080 -BackendPort 8081 -DbPort 3310
```

> **Uwaga:** stack z `docker-compose.yml` jest przeznaczony wyłącznie do lokalnego uruchomienia (demo). Konta demo (tabela niżej) i root MySQL mają znane hasła, a admin może z panelu wyczyścić bazę, dlatego wszystkie porty są publikowane tylko na `127.0.0.1` - aplikacja nie jest dostępna z innych komputerów w sieci. Nie wystawiaj go publicznie bez zmiany haseł i wyłączenia `ALLOW_SEED_REFRESH`.

Zatrzymanie: `docker compose down` (dane bazy zostają w wolumenie `db-data`, pliki załączników w `storage-data`; `docker compose down -v` usuwa je).

Bez skryptu można też użyć bezpośrednio `docker compose up -d --build` - porty brane są wtedy z `.env` (wzór w `.env.example`) lub domyślne. W `.env` muszą wtedy być ustawione `JWT_SECRET`, `S3_ACCESS_KEY` i `S3_SECRET_KEY`.

Zmienne w `.env`:

- `JWT_SECRET` - sekret do podpisywania tokenów JWT (min. 32 znaki). Wymagany: bez niego `docker compose` nie wystartuje. `docker-up.ps1` przy pierwszym uruchomieniu generuje losowy 64-znakowy sekret i zapisuje go w `.env`, przy kolejnych zachowuje istniejący (zmiana sekretu unieważnia zalogowane sesje).
- `S3_ACCESS_KEY`, `S3_SECRET_KEY` - klucze dostępu do magazynu plików załączników (SeaweedFS, usługa `storage`). Wymagane: bez nich `docker compose` nie wystartuje. `docker-up.ps1` generuje losowe (20 i 40 znaków alfanumerycznych) i zachowuje istniejące, jeśli są ciągami co najmniej 16 liter i cyfr - inne zastępuje nowymi z ostrzeżeniem. Własne wartości: tylko litery i cyfry, min. 16 znaków (są wstawiane do pliku konfiguracyjnego S3 generowanego przy starcie kontenera, więc ich zmiana działa po restarcie stacku).
- `ALLOW_SEED_REFRESH` - czy admin może z panelu zresetować dane demo (`POST /api/admin/refresh-seed` usuwa **wszystkich** użytkowników, firmy, oferty i aplikacje i odtwarza dane startowe). W lokalnym stacku Dockera domyślnie `true`; ustaw `false`, żeby wyłączyć. Poza Dockerem reset działa tylko w środowisku Development albo po ustawieniu `Admin:AllowSeedRefresh=true`.
- `SEED_DEMO_ACCOUNTS` - czy seeder tworzy konta demo kandydatów i pracodawców (tabela niżej). W lokalnym stacku Dockera domyślnie `true` (`Seed__DemoAccounts`); ustaw `false`, żeby powstały tylko konta adminów. Poza Dockerem konta demo powstają tylko w środowisku Development, chyba że jawnie ustawiono `Seed:DemoAccounts` (`true`/`false` ma pierwszeństwo). `docker-up.ps1` zachowuje wartość z `.env`.

## Konta testowe

Tworzone automatycznie przy pierwszym starcie (i po resecie danych demo). Hasła są publicznie znane - konta służą tylko do lokalnego demo. Konta adminów powstają zawsze, konta kandydatów i pracodawców demo tylko w Development albo przy `Seed:DemoAccounts=true` (w stacku Dockera domyślnie włączone, patrz `SEED_DEMO_ACCOUNTS`). Konto demo pracodawcy, które istnieje bez firmy (np. po cofnięciu i ponownym zastosowaniu migracji `AddCompanies`), jest przy starcie dopinane do swojej firmy (brakująca firma jest tworzona):

| Rola | E-mail | Hasło |
| --- | --- | --- |
| Admin | `admin@jobboard.com` | `Admin123!` |
| Kandydat | `jan.kowalski@example.com` | `User123!` |
| Pracodawca (firma zatwierdzona: TechCorp Solutions) | `rekruter@techcorp.pl` | `Employer123!` |
| Pracodawca (firma oczekująca: Nowa Firma Sp. z o.o.) | `rekruter@nowafirma.pl` | `Employer123!` |

Po 5 nieudanych próbach logowania konto jest blokowane na 5 minut (API zwraca `423 Locked`). To świadomy kompromis na rzecz czytelnego komunikatu dla użytkownika: odpowiedź `423` ujawnia, że konto o danym adresie istnieje (i jest zablokowane), podczas gdy błędne dane logowania zwracają zawsze `401`.

Tokeny JWT zawierają security stamp użytkownika i są sprawdzane przy każdym żądaniu: token konta, które zostało usunięte (np. przez reset danych demo, który odtwarza też konta z seeda) albo którego security stamp się zmienił (np. po zmianie hasła), jest odrzucany z `401` - trzeba zalogować się ponownie.

## Role i firmy

| Rola | Co może |
| --- | --- |
| Kandydat (`User`) | przegląda oferty i firmy, aplikuje, śledzi i wycofuje swoje aplikacje |
| Pracodawca (`Employer`) | zarządza profilem swojej firmy, jej ofertami i aplikacjami do nich (panel `/employer`); nie aplikuje na oferty |
| Admin (`Admin`) | wszystko: moderuje firmy, zarządza ofertami wszystkich firm i wszystkimi aplikacjami, resetuje dane demo; nie aplikuje na oferty |

Po zalogowaniu (bez `returnUrl`) pracodawca trafia do swojego panelu `/employer`, pozostali na listę ofert; strona „Moje aplikacje” (`/my-applications`) jest tylko dla kandydatów - pracodawca jest przekierowywany do `/employer`, admin do `/admin`.

Rola trafia do tokenu JWT (claim `role`), ale firma pracodawcy jest odczytywana z bazy przy każdym żądaniu, więc zmiana przypisania działa bez ponownego logowania. Sprawdzanie uprawnień do zasobów jest w jednym miejscu (`ICurrentUserAccess`): admin zarządza wszystkim, pracodawca tylko ofertami i aplikacjami swojej firmy - oferta albo aplikacja innej firmy zwraca `404` (bez ujawniania, że istnieje).

### Weryfikacja firm

1. Pracodawca zakłada konto z firmą (`POST /api/auth/register-employer`, przełącznik „Rekrutuję” na stronie rejestracji). Konto i firma powstają w jednej transakcji; nazwa firmy jest unikalna bez rozróżniania wielkości liter i znaków diakrytycznych (kolacja `utf8mb4_0900_ai_ci` - celowo: „Zażółć” i „Zazolc” to ta sama marka), zajęta nazwa zwraca `400`. Przed zapisem nazwa jest normalizowana: niewidoczne znaki formatujące i kontrolne (np. zero-width space) są usuwane, a wszystkie białe znaki Unicode (także twarda spacja) zwijane do jednej spacji i obcinane na brzegach - nazwa złożona wyłącznie z takich znaków jest odrzucana walidacją.
2. Firma ma status **Oczekuje** (`Pending`). Pracodawca może już tworzyć i edytować szkice ofert, ale każdy zapis, po którym oferta byłaby aktywna (`POST`/`PUT`/`PATCH` ze statusem `Active`), zwraca `403` (`Company must be approved before publishing offers`); szkic i zamknięcie oferty są dozwolone. W panelu formularz oferty ogranicza wtedy status do „Szkic” (plus bieżący status edytowanej oferty), a akcje „Opublikuj” / „Wznów” są ukryte.
3. Admin w zakładce „Firmy” zatwierdza firmę (`POST /api/admin/companies/{id}/approve`) albo odrzuca ją z powodem (`POST /api/admin/companies/{id}/reject`). Powód widzi pracodawca w profilu firmy.
4. Odrzucona firma po edycji profilu przez pracodawcę (`PUT /api/employer/company`, także bez zmian - przycisk „Wyślij ponownie”) wraca do statusu **Oczekuje** - to ponowne zgłoszenie do weryfikacji (powód odrzucenia i data decyzji są czyszczone).
5. Zatwierdzona firma wraca do statusu **Oczekuje**, gdy pracodawca zmieni jej nazwę (inaczej niż tylko wielkością liter lub odstępami) albo stronę www - chroni to przed podszyciem się pod inną markę po weryfikacji. Do ponownego zatwierdzenia profil i oferty firmy nie są widoczne publicznie (panel pracodawcy ostrzega o tym przed zapisem). Zmiana samego opisu lub lokalizacji nie zmienia statusu, a edycja przez admina (`PUT /api/admin/companies/{id}`) nigdy go nie zmienia.

Publicznie widoczne są tylko firmy zatwierdzone (`GET /api/companies`, profil `GET /api/companies/{id}`) i tylko ich oferty: odrzucenie firmy ukrywa jej oferty z listy, szczegółów i aplikowania (`404`), ale ich nie usuwa. Admin może też dodać firmę od razu zatwierdzoną, edytować ją i usunąć (tylko firmę bez ofert i bez kont pracodawców). Przy tworzeniu oferty admin wskazuje firmę (`companyId`), pracodawca zawsze publikuje w swojej.

Szczegóły kontraktu API:

- `companyId` oferty: admin musi go podać (brak - `400` z błędem walidacji pola `CompanyId`, nieistniejąca firma - `400` `Company not found`), pracodawcy jest ignorowany (zawsze jego firma). Konto pracodawcy bez firmy (nie powinno wystąpić) dostaje `403` `Employer account is not linked to a company`.
- `CompanyDto`: `status`, `rejectionReason` i `reviewedAt` są wypełniane tylko dla admina i członków firmy (publicznie `null`), `membersCount` tylko w `/api/admin/companies`. `activeJobsCount` to liczba ofert widocznych publicznie (aktywnych i niewygasłych) - dla firmy niezatwierdzonej `0`.
- Pracodawca innej firmy widzi publiczną ofertę (`GET /api/jobs/{id}`) jak każdy; `404` dotyczy operacji zarządzania cudzymi ofertami i aplikacjami.
- W panelu admina filtr firm w zakładce ofert korzysta z `/api/admin/companies` (wszystkie statusy), a wybór firmy w formularzu oferty z `/api/companies` (tylko zatwierdzone).

Migracja `AddCompanies` zamienia tekstową nazwę firmy w ofertach na firmy: dla każdej nazwy (po usunięciu zbędnych odstępów, bez rozróżniania wielkości liter) powstaje zatwierdzona firma (bez daty weryfikacji) z datą utworzenia najstarszej oferty i pisownią nazwy z tej oferty. Cofnięcie migracji przywraca nazwę firmy w ofertach (w ujednoliconej pisowni); przypisania pracodawców i firmy bez ofert są wtedy tracone.

## Cykl życia ofert

Oferta ma status zapisany w bazie oraz opcjonalną datę ważności (`expiresAt`, UTC):

| Stan | Znaczenie | Lista publiczna | Aplikowanie |
| --- | --- | --- | --- |
| Szkic (`Draft`) | oferta w przygotowaniu | nie (szczegóły tylko dla admina i pracodawcy firmy) | nie |
| Aktywna (`Active`) | opublikowana, bez daty lub z datą w przyszłości | tak | tak |
| Wygasła | `Active` z datą ważności w przeszłości - stan wyliczany, nie zapisywany | nie (szczegóły z banerem) | nie |
| Zamknięta (`Closed`) | rekrutacja zakończona | nie (szczegóły z banerem) | nie |

Admin i pracodawca (tylko oferty swojej firmy) zarządzają ofertami w panelu (`GET /api/jobs/manage` z filtrem `state=draft|active|expired|closed`): szybkie akcje Opublikuj / Zamknij / Wznów (`PATCH /api/jobs/{id}/status`) oraz status i datę „Aktualna do” w formularzu oferty (oferta jest ważna do końca wybranego dnia). Aktywnej oferty nie można opublikować, wznowić ani zapisać z nową datą ważności w przeszłości - trzeba ją przedłużyć albo zamknąć; wygasłą ofertę można natomiast edytować bez zmiany daty (pozostaje wygasła). `PUT /api/jobs/{id}` bez pola `status` zachowuje obecny status oferty (`POST` bez statusu tworzy ofertę aktywną). Istniejące oferty po migracji pozostają aktywne i bezterminowe.

Usunięcie oferty (`DELETE /api/jobs/{id}`): admin usuwa ofertę razem z jej aplikacjami, pracodawca tylko ofertę bez żadnych aplikacji (także wycofanych) - ofertę z aplikacjami kandydatów zwraca `400` (`Job offer has applications - close it instead`), można ją natomiast zamknąć.

## Proces rekrutacji

Każda aplikacja ma status, a każda jego zmiana (także złożenie aplikacji) trafia do historii z datą, autorem i opcjonalnym komentarzem:

`Złożona` → `W analizie` → `Rozmowa` → `Oferta` → `Zatrudniony`, w każdej chwili `Odrzucona`; kandydat może też `Wycofać` aplikację.

- **Rekruter (admin albo pracodawca firmy oferty)** zmienia status w panelu (`PUT /api/jobapplications/{id}/status`) i może dodać komentarz - **komentarz jest widoczny dla kandydata**. Ten sam status z komentarzem zapisuje notatkę bez zmiany statusu. Statusu „Wycofana” nie ustawia rekruter, a aplikacji wycofanej nie można już zmieniać. Trwale usunąć aplikację może tylko admin.
- **Kandydat** widzi status i historię w „Moich aplikacjach” (bez danych autorów zmian) i może wycofać aplikację na etapie Złożona / W analizie / Rozmowa / Oferta (`POST /api/jobapplications/{id}/withdraw`). Po wycofaniu może aplikować ponownie, dopóki oferta jest aktywna - reaktywowana zostaje ta sama aplikacja (z nową notatką i datą).
- Aplikować można tylko na oferty aktywne i niewygasłe (próba aplikowania na szkic zwraca dla kandydata `404`, tak jak dla nieistniejącej oferty).
- Równoległe zmiany tej samej aplikacji (także notatki i wycofanie) są chronione tokenem współbieżności: żądanie oparte na nieaktualnym stanie dostaje `409` (`Application status was changed by another request`), a równoległa ponowna aplikacja - `400` (`You have already applied to this job`).

## Załączniki aplikacji

Kandydat może dołączyć do aplikacji kilka plików - CV, list motywacyjny, portfolio albo inny dokument - przy aplikowaniu (najpierw powstaje aplikacja, potem jednym żądaniem wgrywane są pliki) albo później w „Moich aplikacjach”. Dodawać może, dopóki aplikacja jest na etapie Złożona / W analizie / Rozmowa / Oferta; usunąć załącznik może zawsze (także po decyzji końcowej i wycofaniu). Rekruter (admin albo pracodawca firmy oferty) pobiera załączniki z zakładki aplikacji.

| Endpoint | Kto | Wynik |
| --- | --- | --- |
| `POST /api/jobapplications/{id}/attachments` (multipart: pola `files` i równoległe pola `kinds`) | właściciel aplikacji | `201` z aplikacją (lista `attachments`) |
| `DELETE /api/jobapplications/{id}/attachments/{attachmentId}` | właściciel aplikacji, w każdym statusie | `204`; brak załącznika - `404` `Attachment not found` |
| `GET /api/jobapplications/{id}/attachments/{attachmentId}` | właściciel, admin, pracodawca firmy oferty | plik (`Content-Disposition: attachment; filename*=UTF-8''...`, `Content-Length`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`) |

- Rodzaj (`kinds`, liczba): `0` CV, `1` list motywacyjny, `2` portfolio, `3` inny; i-te pole `kinds` dotyczy i-tego pliku, brak (albo za mało pól) - `3`, wartość spoza listy albo więcej pól niż plików - `400` `Invalid attachment kind`. Każda aplikacja w odpowiedziach API (także w listach) ma `attachments: [{ id, kind, fileName, size, uploadedAt }]` (wg rodzaju i daty dodania) i `attachmentsTotalSize`.
- Dozwolone są PDF, DOCX, PNG i JPEG do 5 MB (5 242 880 B) na plik, najwyżej 10 plików i 25 MB (26 214 400 B) łącznie na aplikację (istniejące + nowe). Typ jest rozpoznawany po zawartości (PDF - `%PDF-`; PNG i JPEG - sygnatury; DOCX - archiwum ZIP z `[Content_Types].xml` i częściami `word/`), nie po rozszerzeniu ani typie podanym przez przeglądarkę; `Content-Type` i rozszerzenie nazwy ustala serwer. DOCX nie jest rozpakowywany: przed otwarciem archiwum sprawdzany jest rekord końca katalogu centralnego (bez ZIP64 i archiwów wieloczęściowych, katalog do 256 KB, kończący się tuż przed tym rekordem, maks. 1000 wpisów), a potem katalog (maks. 50 MB zadeklarowanej treści po rozpakowaniu) - inaczej plik jest odrzucany jak nieobsługiwany.
- Wszystko albo nic: najpierw walidowane są wszystkie pliki żądania, potem zapisywane w magazynie, a na końcu - w transakcji z blokadą wiersza aplikacji, która sprawdza limity 10 plików i 25 MB także przy równoległych żądaniach - zapisywane są rekordy. Gdy limit przekroczy równoległe żądanie, zapisane pliki są usuwane. Gdy zapis w bazie skończy się wyjątkiem, usuwane są tylko pliki bez rekordu (wyjątek mógł przyjść już po zatwierdzeniu transakcji); jeśli nie da się tego sprawdzić, pliki zostają (osierocony plik zamiast rekordu bez pliku).
- Błędy (`400`): `Unsupported file type - upload a PDF, DOCX, PNG or JPEG file`, `File is too large - maximum size is 5 MB`, `File is empty`, `Total size of attachments cannot exceed 25 MB`, `An application can have at most 10 attachments`, `No file uploaded - send files in the 'files' form field`, `Invalid multipart form data` (także ucięte żądanie), `Attachments cannot be added in the current application status`. Żądanie z `Content-Length` ponad 26 MB jest odrzucane (`400` `Total size of attachments cannot exceed 25 MB`) bez czytania treści; w stacku Dockera nginx przepuszcza do 26 MB tylko na tym endpoincie (`client_max_body_size 26m`), resztę `/api/` ogranicza do 1 MB.
- Właściciel i status aplikacji są sprawdzane, zanim serwer zacznie czytać treść żądania - cudza albo nieistniejąca aplikacja (także dla pracodawcy innej firmy) zwraca od razu `404`.
- Upload jest ograniczony do 30 żądań na 10 minut na użytkownika (okno stałe liczone od pierwszego żądania, `RateLimiting:Uploads:PermitLimit` i `RateLimiting:Uploads:WindowSeconds`) i do 8 uploadów naraz łącznie dla wszystkich użytkowników (`RateLimiting:Uploads:ConcurrencyLimit`, bez kolejki). Po przekroczeniu - `429` `Too many requests - try again later`; po wyczerpaniu okna z nagłówkiem `Retry-After` = liczba sekund do końca okna. Odrzucenie przez limit równoległości nie zużywa limitu okna. Stan limitów jest w pamięci procesu (przy kilku instancjach backendu - osobno w każdej).
- Nazwa pliku jest zapisywana po sanityzacji (bez ścieżki, znaków sterujących i formatujących, bez kropek i spacji na końcu, nazwy zarezerwowane Windows - `CON`, `NUL`, `COM1`… - z prefiksem `_`, maks. 255 znaków, rozszerzenie zgodne z wykrytym typem) i służy tylko do wyświetlania i pobierania. Plik w magazynie ma klucz generowany przez serwer (`attachments/{applicationId}/{guid}.pdf|docx|png|jpg`), który nie trafia do API.
- Usunięcie załącznika, aplikacji lub oferty przez admina i reset danych demo usuwają też pliki (po zatwierdzeniu zmian w bazie; błąd usuwania pliku jest tylko logowany). Wycofanie aplikacji nie usuwa załączników, a ponowna aplikacja po wycofaniu je zachowuje.

Nieobsłużony błąd serwera (np. awaria magazynu plików) zwraca `500` `{"message":"An unexpected error occurred"}` bez szczegółów - wyjątek trafia tylko do logu (poziom Error). Błąd klienta przy odczycie żądania (`BadHttpRequestException`) dostaje swój kod statusu i ogólny komunikat - `413` `Request body too large`, `408` `Request timed out`, inne `400` `Invalid request` - i jest logowany jako ostrzeżenie.

Treść żądania jest ograniczona do 1 MB (`RequestBodyLimitMiddleware` i globalny `MaxRequestBodySize` Kestrela); endpoint, który przyjmuje pliki, podnosi limit atrybutem `[RequestSizeLimit(bajty)]` (upload załączników - 26 MB). Limit działa także niezależnie od serwera (strumień z limitem), np. w testach na `TestServer` i dla żądań bez `Content-Length`.

### Interfejs i ograniczenia

- Frontend sprawdza typ, rozmiar, liczbę i sumę plików przed wysłaniem (razem z załącznikami, które aplikacja już ma - także przy ponownym aplikowaniu po wycofaniu) i wysyła wszystkie pliki jednym żądaniem z paskiem postępu. Domyślny rodzaj nowego pliku to CV, dopóki aplikacja go nie ma, potem „Inny”. Jeśli wysyłanie plików po złożeniu aplikacji się nie uda albo użytkownik opuści stronę, aplikacja zostaje złożona, a komunikat odsyła do „Moich aplikacji”; Escape w dialogu dodawania przerywa wysyłanie.
- Żądanie bez `Content-Length` jest odrzucane dopiero po przesłaniu 26 MB; Kestrel zrywa wtedy połączenie, więc przeglądarka może nie dostać JSON-a - dlatego frontend waliduje rozmiar wcześniej.
- Model „jedno CV na aplikację” z wcześniejszej wersji tej funkcji nigdy nie trafił do `main` - jego migracja (`AddApplicationCv`) została zastąpiona przez `AddApplicationAttachments`. Bazy deweloperskie, na których ją kiedyś zaaplikowano, mają puste kolumny `Cv*` w `JobApplications` i wpis w `__EFMigrationsHistory` (nieszkodliwe, do ręcznego usunięcia).

### Magazyn plików

Backend zapisuje pliki przez `IFileStorage`, wybierany ustawieniem `Storage:Provider`:

| Ustawienie | Znaczenie |
| --- | --- |
| `Storage:Provider` | `Local` (domyślnie) albo `S3` |
| `Storage:LocalPath` | katalog plików dla `Local`; domyślnie `<katalog aplikacji>/storage` (`Backend/storage`, w `.gitignore`) |
| `Storage:S3:ServiceUrl` | adres magazynu zgodnego z S3, np. `http://storage:8333` (wymagany dla `S3`) |
| `Storage:S3:Bucket` | bucket (domyślnie `jobboard`; tworzony przy starcie backendu, jeśli nie istnieje) |
| `Storage:S3:AccessKey`, `Storage:S3:SecretKey` | klucze dostępu (wymagane dla `S3`) |

Niepełna konfiguracja S3 albo nieznany dostawca przerywają start aplikacji czytelnym błędem. W stacku Dockera backend używa `S3` z usługą `storage` (SeaweedFS 4.50): przy każdym starcie kontenera generowany jest plik konfiguracyjny S3 SeaweedFS (`-s3.config`) z jedną tożsamością o kluczach `S3_ACCESS_KEY` / `S3_SECRET_KEY` z `.env`, więc magazyn nie przyjmuje żądań bez podpisu. Uruchomienie lokalne i testy używają domyślnie `Local`.

## Statistics

Admins and employers get a "Statystyki" tab (lazy-loaded) with KPI tiles, a daily time series, a recruitment funnel, a table of offers, the median first-response time and a CSV export. Admins see the whole platform, an employer only the offers of his own company (the company comes from the database, never from the request).

| Endpoint | Who | Result |
| --- | --- | --- |
| `GET /api/admin/stats?from=&to=` | admin | `range`, `totals` (users by role, companies by status, offers by state, applications by status), daily `series` (`newApplications`, `newJobs`, `newUsers`, `jobViews`), `funnel`, `topJobs` (top 10 of the range), `medianFirstResponseHours` |
| `GET /api/employer/stats?from=&to=` | employer | `range`, `totals` (offers by state, applications by status, all-time `views`), `series` (`newApplications`, `jobViews`), `funnel`, `jobs` (all company offers with `views`, `applications`, `conversionRate`), `medianFirstResponseHours` |
| `GET /api/admin/stats/export`, `GET /api/employer/stats/export` | admin / employer | the daily series as CSV (UTF-8 with BOM, `;` separator, Polish header, `Content-Disposition: attachment; filename*=UTF-8''statystyki-YYYY-MM-DD_YYYY-MM-DD.csv`); cells starting with `=`, `+`, `-`, `@`, tab or CR are prefixed with `'` (CSV injection) |

- `from` and `to` are inclusive UTC days (`YYYY-MM-DD`); the default is the last 30 days ending today. `from > to` or a malformed day returns `400` `Invalid date range`, a range longer than 366 days `400` `Date range cannot exceed 366 days`. An employer without a company gets `403` `Employer account is not linked to a company`.
- `totals` are the current state / all-time values; `series`, `funnel`, `topJobs`, the employer `jobs` table and the median describe the requested range. The series contains every day of the range (missing days are filled with zeros on the server).
- Funnel: applications submitted in the range that reached a stage, according to the status history. Stages are cumulative (`inReview` counts every application whose history contains InReview, Interview, Offered or Hired, and so on); rejection and withdrawal are not stages.
- Median first response: hours from submitting an application (submitted in the range) to the first status change made by someone other than the candidate (a note without a status change does not count); `null` when there is no such application. The per-application differences are computed in SQL and only the one or two middle values are read, so the whole set never reaches memory.
- All aggregations run in SQL (`GROUP BY` on the day, conditional counts); date-range filters use indexes on `JobApplications.AppliedAt`, `JobOffers.CreatedAt` and `AspNetUsers.CreatedAt`.
- Offer views: `GET /api/jobs/{id}` counts one view per (offer, visitor, UTC day) unless the caller is an admin or a member of the offer's company. The visitor is the user id, or the client IP plus User-Agent for anonymous callers; only an HMAC-SHA256 of it (16 bytes, key derived with HKDF from `JwtSettings:Secret`, so the hash cannot be recomputed without the secret) is stored - never the IP. Counting is two statements in one transaction (`INSERT IGNORE` into `JobOfferViewers`, then `INSERT ... ON DUPLICATE KEY UPDATE` of the daily counter in `JobOfferDailyViews`); a failure is only logged and never breaks the offer response. A background service removes deduplication rows older than yesterday (hourly); the daily aggregates stay.
- Behind the nginx container the client IP is taken from `X-Forwarded-For` (one hop) only when the request comes from a private network. Anonymous visitors that share an IP and a User-Agent count as one visitor per day, and a client that varies its User-Agent can inflate the counter - it is a popularity metric, not an audit log.

## Uruchomienie lokalne (bez Dockera)

Wymagane wersje:

- **.NET SDK 9.0** (projekty `net9.0`, ASP.NET Core i EF Core 9.0.x). Przejście na .NET 10 (LTS) czeka na stabilną wersję providera `Pomelo.EntityFrameworkCore.MySql` zgodną z EF Core 10 - najnowsza stabilna (9.0.0) obsługuje tylko EF Core 9.
- **Node.js** `^22.22.3`, `^24.15.0` albo `>=26.0.0` (wymaganie Angulara 22); obraz Dockera frontendu buduje na Node 24 LTS.
- **Angular 22** (Angular CLI 22, Angular Material 22, TypeScript 6.0, testy Vitest).
- **MySQL 8.4** (LTS).

Backend: `dotnet run` w katalogu `Backend` (wymaga MySQL na `localhost:3306`). W środowisku Development, gdy nie podano `JwtSettings:Secret`, używany jest wbudowany klucz deweloperski; w innych środowiskach sekret jest wymagany (np. zmienna `JwtSettings__Secret`). Pliki załączników trafiają domyślnie do `Backend/storage` (patrz „Magazyn plików”). Frontend: `npm start` w katalogu `frontend` - `ng serve` przekazuje `/api` do `http://localhost:5285` (`frontend/proxy.conf.json`).

## Testy

Testy backendu (xUnit) są w katalogu `Backend.Tests` (dodany do `Backend/Backend.sln`): testy jednostkowe (`Unit/`) oraz integracyjne (`Integration/`) uruchamiające aplikację przez `WebApplicationFactory` na prawdziwym MySQL.

```bash
cd Backend.Tests
dotnet test
```

Testy integracyjne wymagają działającego serwera MySQL. Każda fabryka tworzy własną bazę o nazwie `JobBoardTest_<guid>` (migracje i seed przy starcie aplikacji) i usuwa ją po zakończeniu - baza `JobBoardDb` nie jest dotykana. Serwer i dane logowania są brane ze zmiennej środowiskowej `JOBBOARD_TEST_CONNECTION` (connection string MySQL; nazwa bazy jest ignorowana i zastępowana), a gdy jej brak - ze zmiennej `ConnectionStrings__DefaultConnection` (też tylko serwer i dane logowania). Bez żadnej z nich używane jest `Server=localhost;Port=3306;User=root;Password=devpass`.

Przykład: `JOBBOARD_TEST_CONNECTION="Server=localhost;Port=3306;User=root;Password=secret;AllowPublicKeyRetrieval=true;" dotnet test`.

Testy integracyjne zapisują pliki załączników w magazynie `Local` w katalogu tymczasowym osobnym dla każdej fabryki (usuwanym razem z bazą); fabryka testowa podnosi limity uploadów (`RateLimiting:Uploads:PermitLimit` i `ConcurrencyLimit`), a testy limitów używają wartości domyślnych. Testy `S3FileStorage` wymagają magazynu zgodnego z S3 i są pomijane, dopóki nie ustawiono `JOBBOARD_TEST_S3_URL` (np. `http://localhost:8333`; klucze opcjonalnie w `JOBBOARD_TEST_S3_ACCESS_KEY` / `JOBBOARD_TEST_S3_SECRET_KEY`) - każdy test tworzy i usuwa własny bucket.
