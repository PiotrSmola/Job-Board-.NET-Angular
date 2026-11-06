# Job-Board-.NET-Angular

## Uruchomienie w Dockerze

Wymagany Docker Desktop (Docker Compose v2).

```powershell
.\docker-up.ps1
```

Skrypt buduje obrazy i uruchamia pięć kontenerów: MySQL, magazyn plików załączników aplikacji (SeaweedFS z API zgodnym z S3, bez publikowanych portów - dostępny tylko dla backendu w sieci compose), Mailpit (lokalna skrzynka, do której trafiają wszystkie e-maile aplikacji - patrz „Konto użytkownika i e-maile”), backend (ASP.NET Core) i frontend (Angular serwowany przez nginx, który przekazuje `/api` do backendu). Domyślne porty to 4200 (frontend), 5285 (backend / Swagger), 3306 (MySQL) i 8025 (UI Mailpita) - jeśli któryś jest zajęty, wybierany jest kolejny wolny. Wybrane porty zapisywane są do `.env` i wypisywane na koniec.

Preferowane porty można zmienić parametrami:

```powershell
.\docker-up.ps1 -FrontendPort 8080 -BackendPort 8081 -DbPort 3310 -MailpitPort 8026
```

> **Uwaga:** stack z `docker-compose.yml` jest przeznaczony wyłącznie do lokalnego uruchomienia (demo). Konta demo (tabela niżej) i root MySQL mają znane hasła, a admin może z panelu wyczyścić bazę, dlatego wszystkie porty są publikowane tylko na `127.0.0.1` - aplikacja nie jest dostępna z innych komputerów w sieci. Nie wystawiaj go publicznie bez zmiany haseł i wyłączenia `ALLOW_SEED_REFRESH`.

Zatrzymanie: `docker compose down` (dane bazy zostają w wolumenie `db-data`, pliki załączników w `storage-data`; `docker compose down -v` usuwa je).

Bez skryptu można też użyć bezpośrednio `docker compose up -d --build` - porty brane są wtedy z `.env` (wzór w `.env.example`) lub domyślne. W `.env` muszą wtedy być ustawione `JWT_SECRET`, `S3_ACCESS_KEY` i `S3_SECRET_KEY`.

Zmienne w `.env`:

- `JWT_SECRET` - sekret do podpisywania tokenów JWT (min. 32 znaki). Wymagany: bez niego `docker compose` nie wystartuje. `docker-up.ps1` przy pierwszym uruchomieniu generuje losowy 64-znakowy sekret i zapisuje go w `.env`, przy kolejnych zachowuje istniejący (zmiana sekretu unieważnia zalogowane sesje).
- `S3_ACCESS_KEY`, `S3_SECRET_KEY` - klucze dostępu do magazynu plików załączników (SeaweedFS, usługa `storage`). Wymagane: bez nich `docker compose` nie wystartuje. `docker-up.ps1` generuje losowe (20 i 40 znaków alfanumerycznych) i zachowuje istniejące, jeśli są ciągami co najmniej 16 liter i cyfr - inne zastępuje nowymi z ostrzeżeniem. Własne wartości: tylko litery i cyfry, min. 16 znaków (są wstawiane do pliku konfiguracyjnego S3 generowanego przy starcie kontenera, więc ich zmiana działa po restarcie stacku).
- `ALLOW_SEED_REFRESH` - czy admin może z panelu zresetować dane demo (`POST /api/admin/refresh-seed` usuwa **wszystkich** użytkowników, firmy, oferty i aplikacje i odtwarza dane startowe). W lokalnym stacku Dockera domyślnie `true`; ustaw `false`, żeby wyłączyć. Poza Dockerem reset działa tylko w środowisku Development albo po ustawieniu `Admin:AllowSeedRefresh=true`.
- `MAILPIT_PORT` - port UI Mailpita na `127.0.0.1` (domyślnie 8025, `docker-up.ps1` wybiera wolny). Serwer SMTP Mailpita (1025) nie jest publikowany - korzysta z niego tylko backend w sieci compose. Linki w e-mailach prowadzą do `http://localhost:${FRONTEND_PORT}` (`Frontend__BaseUrl`).
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
- Błędy (`400`): `Unsupported file type - upload a PDF, DOCX, PNG or JPEG file`, `File is too large - maximum size is 5 MB`, `File is empty`, `Total size of attachments cannot exceed 25 MB`, `An application can have at most 10 attachments`, `No file uploaded - send files in the 'files' form field`, `Invalid multipart form data` (także ucięte żądanie), `Attachments cannot be added in the current application status`. Żądanie z `Content-Length` ponad 26 MB jest odrzucane (`400` `Total size of attachments cannot exceed 25 MB`) bez czytania treści; w stacku Dockera nginx przepuszcza do 26 MB tylko na tym endpoincie i na `POST /api/account/documents` (`client_max_body_size 26m`), resztę `/api/` ogranicza do 1 MB.
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

## Konto użytkownika i e-maile

Każdy zalogowany ma stronę „Moje konto” (`/account`): profil (imię, nazwisko, telefon, nagłówek, lokalizacja, LinkedIn, opis), zmianę hasła, usunięcie konta (potwierdzone hasłem) i ustawienia powiadomień; kandydat dodatkowo dokumenty profilu (np. domyślne CV) i „Zapisane oferty” (`/saved-jobs`, serduszka na liście i w szczegółach oferty).

| Endpoint | Kto | Wynik |
| --- | --- | --- |
| `GET/PUT /api/account/profile` | zalogowany | profil (`email`, `emailConfirmed`, `roles`, `createdAt` i pola profilu); e-mail nie jest zmieniany |
| `POST /api/account/change-password` `{ currentPassword, newPassword }` | zalogowany | `200` z nowym tokenem (stare tokeny tracą ważność) |
| `DELETE /api/account` `{ password }` | kandydat, pracodawca | `204`; usuwa konto z aplikacjami, załącznikami, dokumentami, zapisanymi ofertami i plikami; admin i ostatni członek firmy z ofertami - `400` |
| `GET/PUT /api/account/notifications` | zalogowany | `{ applicationStatusChanged, newApplication, companyReview }` (domyślnie `true`) |
| `GET/POST /api/account/documents`, `GET/DELETE /api/account/documents/{id}` | kandydat | dokumenty profilu: upload jak załączniki aplikacji (te same typy, 5 MB na plik), najwyżej 10 dokumentów i 25 MB na profil; `POST` zwraca `201` z pełną listą |
| `POST /api/jobapplications/{id}/attachments/from-profile` `{ documentIds }` | właściciel aplikacji | `201`; kopie dokumentów profilu jako załączniki (limity i statusy jak przy uploadzie) |
| `PUT/DELETE /api/saved-jobs/{jobId}`, `GET /api/saved-jobs`, `GET /api/saved-jobs/ids` | kandydat | zapisane oferty (idempotentne `204`; lista od najnowszych z paginacją, także wygasłe i zamknięte oferty) |
| `POST /api/auth/confirm-email` `{ userId, token }` | każdy | `204`; zły lub wygasły link - `400` |
| `POST /api/auth/resend-confirmation` | zalogowany | `204`; adres już potwierdzony - `400` |
| `POST /api/auth/forgot-password` `{ email }` | każdy | zawsze `202` (nie ujawnia, czy konto istnieje) |
| `POST /api/auth/reset-password` `{ email, token, newPassword }` | każdy | `204`; zły lub wygasły link - `400`, słabe hasło - `400` z błędami walidacji |

- **Potwierdzenie adresu**: rejestracja wysyła e-mail z linkiem `<frontend>/confirm-email?userId=…&token=…` (ważny 3 dni, ponowne wysłanie z banera). Niepotwierdzony adres niczego nie blokuje - frontend pokazuje tylko baner (`emailConfirmed` w odpowiedzi logowania i rejestracji).
- **Reset hasła**: link `<frontend>/reset-password?email=…&token=…` jest ważny 1 godzinę i działa raz. Reset, zmiana hasła i usunięcie konta zmieniają security stamp - wcześniej wydane tokeny JWT dostają `401`. Reset potwierdza też adres e-mail i zdejmuje blokadę konta. Tokeny w linkach są zakodowane Base64Url (`+`, `/` i `=` psułyby się w adresie).
- **Powiadomienia** (wg ustawień): kandydat - potwierdzenie złożenia aplikacji i zmiana jej statusu (z komentarzem rekrutera), pracodawcy firmy - nowa aplikacja (bez danych kandydata) i decyzja admina o firmie. E-maile bezpieczeństwa (potwierdzenie, reset) są wysyłane zawsze. Notifications are sent only to confirmed e-mail addresses (an unconfirmed address may belong to someone else); security e-mails (confirmation, password reset) go to unconfirmed addresses too.
- Błędne hasło przy zmianie hasła i usuwaniu konta liczy się do blokady konta tak jak przy logowaniu (`423` po 5 próbach).
- **Limity**: `forgot-password` (per adres IP) i `resend-confirmation` (per użytkownik) - 5 żądań na 15 minut (`RateLimiting:AccountEmails`), potem `429` z `Retry-After`; dodatkowo najwyżej 3 e-maile resetu hasła na konto na godzinę. Upload dokumentów profilu i `from-profile` liczą się do limitu uploadów. Logowanie i rejestracja nie mają limitu żądań (jest tylko blokada konta po nieudanych logowaniach). W stacku Dockera backend widzi adres nginx, więc limit per IP jest wspólny dla całego lokalnego stacku.

### Wysyłka e-maili (outbox) i Mailpit

E-mail nie jest wysyłany w trakcie żądania: trafia do tabeli `EmailMessages` w tej samej transakcji co zdarzenie (np. rejestracja, zmiana statusu aplikacji), a wysyła go w tle `EmailOutboxProcessor` przez SMTP (MailKit). Nieudana próba jest ponawiana z rosnącym opóźnieniem (30 s, 60 s, … do 1 h; najwyżej 8 prób, trwałe odrzucenie przez serwer kończy próby od razu) - status, liczba prób i ostatni błąd są w tabeli. Po wysłaniu (albo ostatecznym błędzie) treść wiadomości jest czyszczona, a wpisy starsze niż 30 dni są usuwane. Wysyłkę obsługuje jedna instancja backendu.

W stacku Dockera wszystkie e-maile trafiają do Mailpita - otwórz `http://localhost:8025` (port wypisuje `docker-up.ps1`), żeby zobaczyć np. link potwierdzający albo link resetu hasła. Nic nie jest wysyłane na zewnątrz.

| Ustawienie | Znaczenie |
| --- | --- |
| `Frontend:BaseUrl` | adres frontendu w linkach (domyślnie `http://localhost:4200`; musi być absolutnym adresem http(s)) |
| `Email:Smtp:Host`, `Email:Smtp:Port` | serwer SMTP; bez hosta e-maile czekają w outboksie (ostrzeżenie w logu) |
| `Email:Smtp:Security` | `None` (np. Mailpit), `StartTls`, `SslOnConnect`, `StartTlsWhenAvailable`, `Auto` (domyślnie) |
| `Email:Smtp:UserName`, `Email:Smtp:Password` | opcjonalne uwierzytelnianie |
| `Email:FromAddress`, `Email:FromName` | nadawca (domyślnie `no-reply@jobboard.local`, „Job Board”) |
| `Email:Outbox:*` | `PollIntervalSeconds` (10), `BatchSize` (20), `MaxAttempts` (8), `InitialRetryDelaySeconds` (30), `MaxRetryDelaySeconds` (3600), `SendTimeoutSeconds` (30), `RetentionDays` (30) |

Klucze Data Protection, którymi podpisywane są tokeny w linkach, są przechowywane w bazie (tabela `DataProtectionKeys`), więc linki działają także po przebudowie kontenera backendu (bez szyfrowania kluczy - ostrzeżenie `No XML encryptor configured` w logu przy starcie; projekt jest lokalny).

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
dotnet test
```

Testy integracyjne wymagają działającego serwera MySQL. Każda fabryka tworzy własną bazę o nazwie `JobBoardTest_<guid>` (migracje i seed przy starcie aplikacji) i usuwa ją po zakończeniu - baza `JobBoardDb` nie jest dotykana. Serwer i dane logowania są brane ze zmiennej środowiskowej `JOBBOARD_TEST_CONNECTION` (connection string MySQL; nazwa bazy jest ignorowana i zastępowana), a gdy jej brak - ze zmiennej `ConnectionStrings__DefaultConnection` (też tylko serwer i dane logowania). Bez żadnej z nich używane jest `Server=localhost;Port=3306;User=root;Password=devpass`.

Przykład: `JOBBOARD_TEST_CONNECTION="Server=localhost;Port=3306;User=root;Password=secret;AllowPublicKeyRetrieval=true;" dotnet test`.

Testy integracyjne zapisują pliki załączników w magazynie `Local` w katalogu tymczasowym osobnym dla każdej fabryki (usuwanym razem z bazą); fabryka testowa podnosi limity uploadów (`RateLimiting:Uploads:PermitLimit` i `ConcurrencyLimit`), a testy limitów używają wartości domyślnych. Testy `S3FileStorage` wymagają magazynu zgodnego z S3 i są pomijane, dopóki nie ustawiono `JOBBOARD_TEST_S3_URL` (np. `http://localhost:8333`; klucze opcjonalnie w `JOBBOARD_TEST_S3_ACCESS_KEY` / `JOBBOARD_TEST_S3_SECRET_KEY`) - każdy test tworzy i usuwa własny bucket.

Fabryka testowa zastępuje wysyłkę SMTP atrapą (`FakeEmailSender` - testy czytają z niej wysłane e-maile, dekodują linki i używają ich w API) i podnosi limit `RateLimiting:AccountEmails`. Test prawdziwej wysyłki SMTP (`MailpitSmtpTests`) wymaga Mailpita i jest pomijany, dopóki nie ustawiono `JOBBOARD_TEST_MAILPIT_URL` (adres API Mailpita, np. `http://localhost:8025`; SMTP na porcie 1025 tego samego hosta).
