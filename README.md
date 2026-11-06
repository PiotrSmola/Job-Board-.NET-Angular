# Job-Board-.NET-Angular

## Uruchomienie w Dockerze

Wymagany Docker Desktop (Docker Compose v2).

```powershell
.\docker-up.ps1
```

The script builds the images and starts five containers: MySQL, the application attachment storage (SeaweedFS with an S3-compatible API, no published ports - reachable only by the backend in the compose network), Mailpit (a local mailbox that receives all e-mails of the application - see "User account and e-mails"), the backend (ASP.NET Core) and the frontend (Angular served by nginx, which forwards `/api` to the backend). The default ports are 4200 (frontend), 5285 (backend / Swagger), 3306 (MySQL) and 8025 (Mailpit UI) - if one is taken, the next free one is used. The chosen ports are saved to `.env` and printed at the end.

Preferowane porty można zmienić parametrami:

```powershell
.\docker-up.ps1 -FrontendPort 8080 -BackendPort 8081 -DbPort 3310 -MailpitPort 8026
```

> **Uwaga:** stack z `docker-compose.yml` jest przeznaczony wyłącznie do lokalnego uruchomienia (demo). Konta demo (tabela niżej) i root MySQL mają znane hasła, a admin może z panelu wyczyścić bazę, dlatego wszystkie porty są publikowane tylko na `127.0.0.1` - aplikacja nie jest dostępna z innych komputerów w sieci. Nie wystawiaj go publicznie bez zmiany haseł i wyłączenia `ALLOW_SEED_REFRESH`.

Zatrzymanie: `docker compose down` (dane bazy zostają w wolumenie `db-data`, pliki załączników w `storage-data`; `docker compose down -v` usuwa je).

Bez skryptu można też użyć bezpośrednio `docker compose up -d --build` - porty brane są wtedy z `.env` (wzór w `.env.example`) lub domyślne. W `.env` muszą wtedy być ustawione `JWT_SECRET`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` i `ADMIN_PASSWORD`.

Zmienne w `.env`:

- `JWT_SECRET` - sekret do podpisywania tokenów JWT (min. 32 znaki). Wymagany: bez niego `docker compose` nie wystartuje. `docker-up.ps1` przy pierwszym uruchomieniu generuje losowy 64-znakowy sekret i zapisuje go w `.env`, przy kolejnych zachowuje istniejący (zmiana sekretu unieważnia zalogowane sesje).
- `S3_ACCESS_KEY`, `S3_SECRET_KEY` - klucze dostępu do magazynu plików załączników (SeaweedFS, usługa `storage`). Wymagane: bez nich `docker compose` nie wystartuje. `docker-up.ps1` generuje losowe (20 i 40 znaków alfanumerycznych) i zachowuje istniejące, jeśli są ciągami co najmniej 16 liter i cyfr - inne zastępuje nowymi z ostrzeżeniem. Własne wartości: tylko litery i cyfry, min. 16 znaków (są wstawiane do pliku konfiguracyjnego S3 generowanego przy starcie kontenera, więc ich zmiana działa po restarcie stacku).
- `ADMIN_PASSWORD` - password of the admin accounts (`admin@jobboard.com`, `superadmin@jobboard.com`) created by the seeder (`Seed__AdminPassword`). Required: `docker compose` does not start without it. On the first run `docker-up.ps1` generates a random 20-character password and stores it in `.env`, later runs keep the existing one, and the script **prints it at the end** (`Admin: admin@jobboard.com / ...`). A custom value must be letters and digits only, at least 16 characters, with a lowercase letter, an uppercase letter and a digit - otherwise the script replaces it with a new one and prints a warning. The password applies only to admin accounts created with this setting (a new database or a demo data reset): **existing admin accounts in existing databases (the `db-data` volume) keep their current password** (e.g. `Admin123!` from earlier versions) - to switch to the password from `.env`, reset the demo data from the admin panel or remove the volume (`docker compose down -v`). Outside Docker: Development defaults to `Admin123!`; other environments must set `Seed:AdminPassword` to create the admin accounts (a missing password or one that breaks the password policy stops the startup with a clear error).
- `ALLOW_SEED_REFRESH` - czy admin może z panelu zresetować dane demo (`POST /api/admin/refresh-seed` usuwa **wszystkich** użytkowników, firmy, oferty i aplikacje i odtwarza dane startowe). W lokalnym stacku Dockera domyślnie `true`; ustaw `false`, żeby wyłączyć. Poza Dockerem reset działa tylko w środowisku Development albo po ustawieniu `Admin:AllowSeedRefresh=true`.
- `MAILPIT_PORT` - port UI Mailpita na `127.0.0.1` (domyślnie 8025, `docker-up.ps1` wybiera wolny). Serwer SMTP Mailpita (1025) nie jest publikowany - korzysta z niego tylko backend w sieci compose. Linki w e-mailach prowadzą do `http://localhost:${FRONTEND_PORT}` (`Frontend__BaseUrl`).
- `SEED_DEMO_ACCOUNTS` - czy seeder tworzy konta demo kandydatów i pracodawców (tabela niżej). W lokalnym stacku Dockera domyślnie `true` (`Seed__DemoAccounts`); ustaw `false`, żeby powstały tylko konta adminów. Poza Dockerem konta demo powstają tylko w środowisku Development, chyba że jawnie ustawiono `Seed:DemoAccounts` (`true`/`false` ma pierwszeństwo). `docker-up.ps1` zachowuje wartość z `.env`.
- `SWAGGER_ENABLED` - whether the backend serves Swagger UI (`http://localhost:<BACKEND_PORT>/swagger`). Defaults to `true` in the local Docker stack (`Swagger__Enabled`); set `false` to turn it off. Outside Docker Swagger is always available in Development and in other environments only with `Swagger:Enabled=true`. `docker-up.ps1` keeps the value from `.env`.

## Konta testowe

Tworzone automatycznie przy pierwszym starcie (i po resecie danych demo). Hasła są publicznie znane - konta służą tylko do lokalnego demo. Konta adminów powstają zawsze, konta kandydatów i pracodawców demo tylko w Development albo przy `Seed:DemoAccounts=true` (w stacku Dockera domyślnie włączone, patrz `SEED_DEMO_ACCOUNTS`). Konto demo pracodawcy, które istnieje bez firmy (np. po cofnięciu i ponownym zastosowaniu migracji `AddCompanies`), jest przy starcie dopinane do swojej firmy (brakująca firma jest tworzona):

| Rola | E-mail | Hasło |
| --- | --- | --- |
| Admin | `admin@jobboard.com` | Development: `Admin123!`; Docker: `ADMIN_PASSWORD` from `.env` (printed by `docker-up.ps1`) |
| Kandydat | `jan.kowalski@example.com` | `User123!` |
| Pracodawca (firma zatwierdzona: TechCorp Solutions) | `rekruter@techcorp.pl` | `Employer123!` |
| Pracodawca (firma oczekująca: Nowa Firma Sp. z o.o.) | `rekruter@nowafirma.pl` | `Employer123!` |

After 5 failed login attempts the account is locked for 5 minutes. The response does not reveal whether an account with the given email exists: a wrong password, an unknown account and a wrong password for a locked account always return `401` `Invalid email or password` in comparable time (for an unknown account the password is verified against a dummy hash). Only someone who gives the **correct** password of a locked account gets `423 Locked`. Attempts during the lockout neither change the counter nor extend the lockout. Parallel login attempts for one account run one at a time (an in-process lock per email), so the failed-attempt counter never loses an attempt.


JWT tokens (HS256, valid for 120 minutes - `JwtSettings:ExpirationInMinutes`, no refresh; 30 s clock skew) carry a hash of the user's security stamp (the first 16 bytes of its SHA-256, not the stamp itself - the JWT payload is readable) and are checked on every request: a token of an account that was deleted (e.g. by the demo data reset, which also recreates the seeded accounts) or whose security stamp changed (e.g. after a password change) is rejected with `401` - the user has to log in again.

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
- Błędy (`400`): `Unsupported file type - upload a PDF, DOCX, PNG or JPEG file`, `File is too large - maximum size is 5 MB`, `File is empty`, `Total size of attachments cannot exceed 25 MB`, `An application can have at most 10 attachments`, `No file uploaded - send files in the 'files' form field`, `Invalid multipart form data` (także ucięte żądanie), `Attachments cannot be added in the current application status`. Żądanie z `Content-Length` ponad 26 MB jest odrzucane (`400` `Total size of attachments cannot exceed 25 MB`) bez czytania treści; w stacku Dockera nginx przepuszcza do 26 MB tylko na tym endpoincie i na `POST /api/account/documents` (`client_max_body_size 26m`), resztę `/api/` ogranicza do 1 MB.
- Właściciel i status aplikacji są sprawdzane, zanim serwer zacznie czytać treść żądania - cudza albo nieistniejąca aplikacja (także dla pracodawcy innej firmy) zwraca od razu `404`.
- Upload jest ograniczony do 30 żądań na 10 minut na użytkownika (okno stałe liczone od pierwszego żądania, `RateLimiting:Uploads:PermitLimit` i `RateLimiting:Uploads:WindowSeconds`) i do 8 uploadów naraz łącznie dla wszystkich użytkowników (`RateLimiting:Uploads:ConcurrencyLimit`, bez kolejki). Po przekroczeniu - `429` `Too many requests - try again later`; po wyczerpaniu okna z nagłówkiem `Retry-After` = liczba sekund do końca okna (a rejection by the concurrency limit carries `Retry-After: 1`). Odrzucenie przez limit równoległości nie zużywa limitu okna. Stan limitów jest w pamięci procesu (przy kilku instancjach backendu - osobno w każdej).
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

## User account and e-mails

Every signed-in user has a "Moje konto" page (`/account`): the profile (first and last name, phone, headline, location, LinkedIn, about), password change, account deletion (confirmed with the password) and notification settings; candidates also get profile documents (e.g. a default CV) and "Zapisane oferty" (`/saved-jobs`, heart buttons on the list and on the offer details).

| Endpoint | Who | Result |
| --- | --- | --- |
| `GET/PUT /api/account/profile` | signed in | the profile (`email`, `emailConfirmed`, `roles`, `createdAt` and the profile fields); the e-mail address cannot be changed |
| `POST /api/account/change-password` `{ currentPassword, newPassword }` | signed in | `200` with a new token (old tokens stop working) |
| `DELETE /api/account` `{ password }` | candidate, employer | `204`; deletes the account with its applications, attachments, documents, saved offers and files; an admin and the last member of a company with offers get `400` |
| `GET/PUT /api/account/notifications` | signed in | `{ applicationStatusChanged, newApplication, companyReview }` (all `true` by default) |
| `GET/POST /api/account/documents`, `GET/DELETE /api/account/documents/{id}` | candidate | profile documents: uploaded like application attachments (the same types, 5 MB per file), at most 10 documents and 25 MB per profile; `POST` returns `201` with the full list |
| `POST /api/jobapplications/{id}/attachments/from-profile` `{ documentIds }` | application owner | `201`; copies profile documents as attachments (the same limits and statuses as an upload) |
| `PUT/DELETE /api/saved-jobs/{jobId}`, `GET /api/saved-jobs`, `GET /api/saved-jobs/ids` | candidate | saved offers (idempotent `204`; the list is paged, newest first, and includes expired and closed offers) |
| `POST /api/auth/confirm-email` `{ userId, token }` | anyone | `204`; an invalid or expired link - `400` |
| `POST /api/auth/resend-confirmation` | signed in | `204`; an already confirmed address - `400` |
| `POST /api/auth/forgot-password` `{ email }` | anyone | always `202` (does not reveal whether the account exists) |
| `POST /api/auth/reset-password` `{ email, token, newPassword }` | anyone | `204`; an invalid or expired link - `400`, a weak password - `400` with validation errors |

- **Address confirmation**: registration sends an e-mail with a `<frontend>/confirm-email?userId=…&token=…` link (valid for 3 days, can be resent from the banner). An unconfirmed address blocks nothing - the frontend only shows a banner (`emailConfirmed` in the login and registration responses).
- **Password reset**: the `<frontend>/reset-password?email=…&token=…` link is valid for 1 hour and works once. A reset, a password change and an account deletion change the security stamp - JWT tokens issued earlier get `401`. A reset also confirms the e-mail address and lifts the account lockout. The tokens in the links are Base64Url-encoded (`+`, `/` and `=` would break in a URL).
- **Notifications** (according to the settings): the candidate - confirmation of a submitted application and its status changes (with the recruiter's comment), the company's employers - a new application (without the candidate's data) and the admin's decision about the company. Notifications are sent only to confirmed e-mail addresses (an unconfirmed address may belong to someone else); security e-mails (confirmation, password reset) are always sent, to unconfirmed addresses too.
- A wrong password when changing the password or deleting the account counts towards the account lockout just like a failed login (`423` after 5 attempts).
- **Limits**: `forgot-password` (per client IP) and `resend-confirmation` (per user) - 5 requests per 15 minutes (`RateLimiting:AccountEmails`), then `429` with `Retry-After`; on top of that at most 3 password reset e-mails per account per hour. These two endpoints use only the `account-emails` policy - their own `[EnableRateLimiting]` replaces the `auth` policy of `AuthController` (ASP.NET Core applies the attribute closest to the endpoint, so the limits do not add up); the other `AuthController` endpoints, including `confirm-email` and `reset-password`, fall under `auth` (see "Security"). The client IP is the real address from `X-Forwarded-For` (see "Security"), so in the Docker stack every client behind nginx has its own limit. Profile document uploads and `from-profile` count towards the upload limit.

### Sending e-mails (outbox) and Mailpit

An e-mail is not sent during the request: it is stored in the `EmailMessages` table in the same transaction as the event (e.g. a registration or an application status change) and sent in the background by `EmailOutboxProcessor` over SMTP (MailKit). A failed attempt is retried with a growing delay (30 s, 60 s, … up to 1 h; at most 8 attempts, a permanent rejection by the server stops the retries at once) - the status, the number of attempts and the last error are kept in the table. After sending (or the final failure) the message body is cleared, and entries older than 30 days are deleted. Sending is handled by a single backend instance.

In the Docker stack all e-mails go to Mailpit - open `http://localhost:8025` (`docker-up.ps1` prints the port) to see e.g. the confirmation link or the password reset link. Nothing is sent outside.

| Setting | Meaning |
| --- | --- |
| `Frontend:BaseUrl` | the frontend address used in the links (default `http://localhost:4200`; must be an absolute http(s) URL) |
| `Email:Smtp:Host`, `Email:Smtp:Port` | the SMTP server; without a host the e-mails wait in the outbox (a warning in the log) |
| `Email:Smtp:Security` | `None` (e.g. Mailpit), `StartTls`, `SslOnConnect`, `StartTlsWhenAvailable`, `Auto` (default) |
| `Email:Smtp:UserName`, `Email:Smtp:Password` | optional authentication |
| `Email:FromAddress`, `Email:FromName` | the sender (default `no-reply@jobboard.local`, "Job Board") |
| `Email:Outbox:*` | `PollIntervalSeconds` (10), `BatchSize` (20), `MaxAttempts` (8), `InitialRetryDelaySeconds` (30), `MaxRetryDelaySeconds` (3600), `SendTimeoutSeconds` (30), `RetentionDays` (30) |

The Data Protection keys that sign the tokens in the links are stored in the database (the `DataProtectionKeys` table), so the links keep working after the backend container is rebuilt (the keys are not encrypted - hence the `No XML encryptor configured` warning in the log at startup; the project is local only).

## Security

The project runs locally only (a portfolio project, the Docker stack listens on `127.0.0.1`), but the backend and nginx are hardened as if the application were exposed to a network. This section lists what is protected and the deliberate trade-offs that follow from the local scope.

### What is protected

- **Login and registration** (the whole `AuthController`, rate limiting policy `auth` - new authentication endpoints such as e-mail confirmation and password reset inherit it automatically; only `forgot-password` and `resend-confirmation` use the `account-emails` policy instead - see "User account and e-mails"): at most 10 requests per minute per client IP (`RateLimiting:Auth:PermitLimit`, `RateLimiting:Auth:WindowSeconds`) and 4 at once overall (`RateLimiting:Auth:ConcurrencyLimit`, no queue - password hashing is CPU-heavy). Exceeding a limit returns `429` `Too many requests - try again later` with `Retry-After` (seconds until the end of the window, `1` for the concurrency limit). The limits are validated at startup (a value below 1 stops the startup).
- **Account lockout** after 5 failed attempts for 5 minutes without account enumeration (see the paragraph under the test accounts): an unknown account and a wrong password give the same `401` in comparable time, `423` only with the correct password. Parallel attempts for one account are serialized (a lock per normalized email whose entries are removed after use), so the counter never loses an attempt.
- **Input validation**: email up to 256 characters and password up to 100 in every DTO with credentials (login, registration, password change and reset, account deletion - `CredentialLimits`, the same password limit as in the frontend forms), search terms and the location filter of the lists up to 100 characters (`400`), page numbers capped at 100,000 (a page past the end is empty - `200`, not `500`).
- **Public lists** (`GET /api/jobs`, `GET /api/companies`, including search): 120 requests per minute per client IP (`RateLimiting:PublicLists:*`); the job list is backed by the composite index `(Status, ExpiresAt, CreatedAt)`.
- **Client IP behind nginx**: the per-IP limits see the real address - the backend reads `X-Forwarded-For` (`UseForwardedHeaders`, last entry only, i.e. the one appended by nginx, so spoofed earlier entries do not bypass the limit).
- **JWT**: HS256 only, 30 s clock skew, 120-minute lifetime (`JwtSettings:ExpirationInMinutes`), a hash of the security stamp instead of the stamp itself; every request checks that the account still exists and the stamp is current.
- **Headers** - nginx (frontend and proxied API): `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (no camera, microphone, geolocation, payment, USB), `Cross-Origin-Opener-Policy: same-origin`, no nginx version (`server_tokens off`); gzip compression; files with a content hash in the name (`*.js`, `*.css`, `*.woff2` from the build) get `Cache-Control: public, max-age=31536000, immutable`, `index.html` gets `no-cache`. The headers are set only at `server` level (`add_header` in a `location` block drops all inherited ones - guarded by `NginxConfigTests`). Backend: no `Server` header, `X-Content-Type-Options: nosniff` on every API response, `Cache-Control: no-store` on responses with user data (requests with a token and endpoints that require login).
- **CSP**: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`. Scripts come only from the own origin, with no exceptions: the production build does not inline critical CSS (`optimization.styles.inlineCritical: false` in `angular.json`), because that feature adds an inline script to `index.html` that switches the stylesheet on. Alternatives considered: the hash of that script in the header (fragile - it changes with the Angular version, and a stale hash leaves the page unstyled) and `security.autoCsp` (a `<meta>` CSP with the hash of a loader script that contains the `main-<hash>.js` name, so it changes with every build - the static nginx header would then have to allow `'unsafe-inline'`). The cost: the stylesheet (about 6 kB compressed) is render-blocking, which is negligible for an SPA that renders only after `main.js` anyway. `'unsafe-inline'` in `style-src` is needed because Angular and Angular Material inject component styles.
- **Swagger** only in Development or with `Swagger:Enabled=true` (`SWAGGER_ENABLED` in the Docker stack, default `true`). The backend does not use `UseHttpsRedirection` (it serves HTTP behind nginx).
- **Containers**: the backend runs as the `app` user of the .NET image (the `Local` storage directory belongs to it), every service runs with `no-new-privileges`, ports are published on `127.0.0.1` only, secrets (`JWT_SECRET`, S3 keys, `ADMIN_PASSWORD`) are random and kept in `.env` (outside the repository).
- **Logs** without SQL statements (`Microsoft.EntityFrameworkCore.Database.Command` at `Warning`); server errors without details in the response.

### Deliberate trade-offs

- **HTTP without TLS and HSTS** - the stack listens on `127.0.0.1` only; certificates and HSTS make no sense locally.
- **Rate limits and login locks live in process memory** - a single backend instance; a restart resets the rate limit counters (the account lockout is stored in the database and survives it). Several instances would each count separately.
- **`X-Forwarded-For` is accepted from any sender** (`KnownNetworks`/`KnownProxies` are cleared because the nginx address in the Docker network is not fixed). The backend is reachable only through nginx and directly on the host's `127.0.0.1` - a local process calling the backend past nginx can send any address and bypass the per-IP limit.
- **`423` with the correct password** reveals the lockout to whoever knows the password - this removes account enumeration, but during the lockout the response still tells a correct password from a wrong one, so guessing the password of a locked account is limited by the per-IP limit (10 attempts per minute), not by the lockout itself.
- **No `limit_req` in nginx** - the backend enforces the per-IP limits (a uniform JSON `429` with `Retry-After`); a second limit in nginx would answer with an HTML page without `Retry-After`.
- **Google Fonts** - the browser downloads fonts from `fonts.gstatic.com` (the build inlines the font CSS into `index.html`), so the CSP allows `fonts.googleapis.com` and `fonts.gstatic.com`. A later clean-up stage will bundle the fonts with the app and drop these sources from the CSP.
- **`ALLOW_SEED_REFRESH=true` by default in Docker** (a convenient demo reset). The risk chain: whoever takes over an admin account (e.g. the old `Admin123!` password in a database created before `ADMIN_PASSWORD`, a stolen token, an XSS flaw) can delete all users, companies, offers, applications and attachment files with a single `POST /api/admin/refresh-seed`, and after the reset the demo accounts with publicly known passwords exist again. With your own data set `ALLOW_SEED_REFRESH=false` (and `SEED_DEMO_ACCOUNTS=false`).
- **MySQL as `root`** - the backend connects with the root account. A separate application user (`MYSQL_USER`) is created only when an empty volume is initialized and existing `db-data` volumes do not have one - introducing it would require a manual data migration. The database port is published on `127.0.0.1` only and `DB_PASSWORD` sets the root password.
- **Demo accounts and admin passwords in older databases** - the demo accounts have publicly known passwords (turned off with `SEED_DEMO_ACCOUNTS=false`), and admin accounts created before `ADMIN_PASSWORD` keep `Admin123!` until a demo data reset or removal of the volume.
- **Swagger enabled by default in Docker** - the API documentation is part of the portfolio (turn it off with `SWAGGER_ENABLED=false`).
- **No token refresh** - after 120 minutes the user has to log in again.

## Uruchomienie lokalne (bez Dockera)

Wymagane wersje:

- **.NET SDK 9.0** (projekty `net9.0`, ASP.NET Core i EF Core 9.0.x). Przejście na .NET 10 (LTS) czeka na stabilną wersję providera `Pomelo.EntityFrameworkCore.MySql` zgodną z EF Core 10 - najnowsza stabilna (9.0.0) obsługuje tylko EF Core 9.
- **Node.js** `^22.22.3`, `^24.15.0` albo `>=26.0.0` (wymaganie Angulara 22); obraz Dockera frontendu buduje na Node 24 LTS.
- **Angular 22** (Angular CLI 22, Angular Material 22, TypeScript 6.0, testy Vitest).
- **MySQL 8.4** (LTS).

Backend: `dotnet run` w katalogu `Backend` (wymaga MySQL na `localhost:3306`). W środowisku Development, gdy nie podano `JwtSettings:Secret`, używany jest wbudowany klucz deweloperski; w innych środowiskach sekret jest wymagany (np. zmienna `JwtSettings__Secret`). Pliki załączników trafiają domyślnie do `Backend/storage` (patrz „Magazyn plików”). E-maile są wysyłane tylko po ustawieniu `Email:Smtp:Host` - np. z lokalnym Mailpitem (`docker run -p 8025:8025 -p 1025:1025 axllent/mailpit:v1.31.1`): `Email__Smtp__Host=localhost`, `Email__Smtp__Port=1025`, `Email__Smtp__Security=None`. Frontend: `npm start` w katalogu `frontend` - `ng serve` przekazuje `/api` do `http://localhost:5285` (`frontend/proxy.conf.json`).

## Testy

Testy backendu (xUnit) są w katalogu `Backend.Tests` (dodany do `Backend/Backend.sln`): testy jednostkowe (`Unit/`) oraz integracyjne (`Integration/`) uruchamiające aplikację przez `WebApplicationFactory` na prawdziwym MySQL.

```bash
cd Backend.Tests
# full suite
dotnet test
# fast suite - without the migration tests (each creates and migrates its own databases)
dotnet test --filter "Category!=Migrations"
# migration tests only
dotnet test --filter "Category=Migrations"
```

Migration test classes carry `[Trait("Category", "Migrations")]` (checked by `TestInfrastructureTests`) - every new `*MigrationTests` class needs it too.

Testy integracyjne wymagają działającego serwera MySQL. Każda fabryka tworzy własną bazę o nazwie `JobBoardTest_<guid>` (migracje i seed przy starcie aplikacji) i usuwa ją po zakończeniu - baza `JobBoardDb` nie jest dotykana. Serwer i dane logowania są brane ze zmiennej środowiskowej `JOBBOARD_TEST_CONNECTION` (connection string MySQL; nazwa bazy jest ignorowana i zastępowana), a gdy jej brak - ze zmiennej `ConnectionStrings__DefaultConnection` (też tylko serwer i dane logowania). Bez żadnej z nich używane jest `Server=localhost;Port=3306;User=root;Password=devpass`.

Przykład: `JOBBOARD_TEST_CONNECTION="Server=localhost;Port=3306;User=root;Password=secret;AllowPublicKeyRetrieval=true;" dotnet test`.

Integration tests store attachment files in the `Local` storage, in a temporary directory separate for each factory (deleted together with its database). The test factory raises the upload limits (`RateLimiting:Uploads:PermitLimit` and `ConcurrencyLimit`), the login limits (`RateLimiting:Auth:*` - all test requests share one client address), the public list limit (`RateLimiting:PublicLists:PermitLimit`) and the security e-mail limit (`RateLimiting:AccountEmails:PermitLimit`); the tests of these limits use the defaults and pass client addresses in `X-Forwarded-For` where they need separate ones. Outside Development the factory sets `Seed:AdminPassword` to the Development admin password. The `S3FileStorage` tests need an S3-compatible storage and are skipped until `JOBBOARD_TEST_S3_URL` is set (e.g. `http://localhost:8333`; keys optionally in `JOBBOARD_TEST_S3_ACCESS_KEY` / `JOBBOARD_TEST_S3_SECRET_KEY`) - each test creates and deletes its own bucket.

The test factory replaces SMTP sending with a fake (`FakeEmailSender` - the tests read the sent e-mails from it, decode the links and use them against the API). The real SMTP test (`MailpitSmtpTests`) needs Mailpit and is skipped until `JOBBOARD_TEST_MAILPIT_URL` is set (the Mailpit API address, e.g. `http://localhost:8025`; SMTP on port 1025 of the same host).
