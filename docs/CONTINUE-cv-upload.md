# Etap 3 v2 – Załączniki aplikacji (feature/cv-upload) – KONTRAKT

Decyzja użytkownika: aplikacja ma WIELE załączników (CV, list motywacyjny, portfolio, inne). Limit: **5 MB na plik, 25 MB łącznie na aplikację**. Zastępuje model „jedno CV” z wersji 1 (kolumny `Cv*` w `JobApplications` i endpointy `/cv` znikają). Zachowujemy z v1: `IFileStorage` (Local/S3), SeaweedFS w compose, walidację po zawartości, sanityzację nazw, nagłówki pobierania, sprzątanie plików, autoryzację przez `ICurrentUserAccess`.

## Model i migracja
- `enum AttachmentKind { Cv = 0, CoverLetter = 1, Portfolio = 2, Other = 3 }`.
- Encja `ApplicationAttachment { Id, JobApplicationId (FK cascade), Kind, FileKey (max 300, unikalny), FileName (max 255), ContentType (max 100), Size (long), UploadedAt (UTC) }`, indeks na `JobApplicationId`.
- Migracje: gałąź nie jest jeszcze w `main`, więc migrację `AddApplicationCv` z v1 USUŃ (najpierw cofnij ją na dev `JobBoardDb`: `dotnet ef database update <poprzednia>`; potem `dotnet ef migrations remove`) i utwórz jedną `AddApplicationAttachments`. Snapshot musi być spójny.
- Klucze plików: `attachments/{applicationId}/{guid}{ext}` (generowane przez serwer).

## Limity i walidacja
- Dozwolone typy (wykrywane po zawartości): PDF (`%PDF-`), DOCX (ZIP z `[Content_Types].xml` i `word/`), PNG (`89 50 4E 47 0D 0A 1A 0A`), JPEG (`FF D8 FF`). Content type ustala serwer.
- DOCX – ochrona przed zip bombą i wzmocnieniem CPU: PRZED `new ZipArchive` odczytaj z EOCD rozmiar i offset katalogu centralnego; odrzuć, gdy rozmiar katalogu > 256 KB, offset+rozmiar poza plikiem, ZIP64 (0xFFFF/0xFFFFFFFF), deklarowana liczba wpisów > 1000; po otwarciu dalej limit wpisów (1000) i łącznego zadeklarowanego rozmiaru po rozpakowaniu (50 MB). Nic nie rozpakowujemy.
- Plik: 1 B – 5 MB (5 242 880 B). Łącznie na aplikację (istniejące + nowe): ≤ 25 MB (26 214 400 B). Maks. 10 załączników na aplikację.
- Komunikaty (400): `Unsupported file type - upload a PDF, DOCX, PNG or JPEG file`, `File is too large - maximum size is 5 MB`, `File is empty`, `Total size of attachments cannot exceed 25 MB`, `An application can have at most 10 attachments`, `No file uploaded - send files in the 'files' form field`, `Invalid multipart form data` (również ucięty multipart / `IOException` / `BadHttpRequestException`), `Attachments cannot be added in the current application status`.
- Limit żądania: 26 MB (Kestrel `MaxRequestBodySize` dla endpointu + `FormOptions.MultipartBodyLengthLimit`); nginx `client_max_body_size 26m;` w `location /api/` (frontend/docker/nginx.conf).
- Kontrola właściciela i statusu aplikacji PRZED odczytem ciała (lekkie zapytanie w filtrze lub ręczne czytanie formularza w akcji po autoryzacji), żeby obcy nie wysyłali 26 MB na cudze id.
- Rate limiting (ASP.NET `AddRateLimiter`): polityka `uploads` – per zalogowany użytkownik, np. 30 żądań / 10 min (sliding/fixed window) na POST załączników → 429 `{message:"Too many requests - try again later"}`. Konfigurowalne w `RateLimiting:*`; w testach możliwe do podniesienia.

## Endpointy
- `POST /api/jobapplications/{id}/attachments` – multipart: jedno lub więcej pól `files` oraz równoległa lista pól `kinds` (liczby enum; brak/za mało → `Other`; zła wartość → 400). Tylko właściciel; statusy Submitted/InReview/Interview/Offered. Walidacja WSZYSTKICH plików przed zapisem czegokolwiek (wszystko albo nic). Sprawdzenie limitu łącznego i liczby pod blokadą wiersza aplikacji (`SELECT ... FOR UPDATE` lub mechanizm z etapu 1) – równoległe żądania nie przekroczą 25 MB / 10 plików. Zapis plików → insert rekordów → przy błędzie bazy usunięcie zapisanych plików. → 201 `JobApplicationDto` (z listą załączników). Cudza/nieistniejąca → 404.
- `DELETE /api/jobapplications/{id}/attachments/{attachmentId}` – tylko właściciel, W KAŻDYM STATUSIE (prawo do usunięcia danych) → 204; plik usuwany po commicie. Brak → 404 `Attachment not found`.
- `GET /api/jobapplications/{id}/attachments/{attachmentId}` – właściciel, admin, pracodawca firmy oferty; inaczej 404. Strumień, `Content-Type` z bazy, `Content-Disposition` przez `ContentDispositionHeaderValue` (tylko `filename*`), `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, `Content-Length` jeśli znany (także z S3). Strumień zwalniany również przy wyjątku.
- Endpointy `/cv` z v1 USUNIĘTE.
- Sprzątanie jak w v1: usunięcie aplikacji / oferty (admin), RefreshSeed (`DeleteByPrefixAsync("attachments/")`), reaktywacja zachowuje załączniki, wycofanie nie usuwa.

## DTO
- `JobApplicationDto`: usuń `hasCv`, `cvFileName`, `cvSize`, `cvUploadedAt`; dodaj `attachments: [{ id, kind, fileName, size, uploadedAt }]` (posortowane: kind, uploadedAt) i `attachmentsTotalSize`. Klucz pliku NIGDY w DTO. W listach (`my-applications`, `/all`, `/job/{id}`) też `attachments` (bez N+1 – jedno zapytanie/projekcja).

## Pozostałe poprawki z review v1 (backend/infrastruktura)
- `Backend/.dockerignore` += `storage/`.
- `docker-up.ps1`: istniejące `S3_ACCESS_KEY`/`S3_SECRET_KEY` muszą pasować do `^[A-Za-z0-9]{16,}$`, inaczej wygeneruj nowe z ostrzeżeniem (BOM zachowany).
- `DeleteQuietlyAsync` z krótkim tokenem (np. 10 s), nie `CancellationToken.None`.
- `LocalFileStorage`: `Flush(true)` przed `Move`; plik tymczasowy w tym samym katalogu z unikalną nazwą, usuwany przy błędzie; sprzątanie osieroconych `*.tmp` starszych niż 1 h przy `InitializeAsync`.
- `SanitizeFileName`: nazwy zarezerwowane Windows (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`) → prefiks `_`.
- Komentarz o `ValueLengthLimit` – popraw lub usuń mylący.

## Frontend
- Model/serwis: `attachments`, `AttachmentKind` + polskie etykiety (CV / List motywacyjny / Portfolio / Inny); `uploadAttachments(id, items: {file, kind}[])` (jedno żądanie multipart, `reportProgress`), `deleteAttachment(id, attachmentId)`, `downloadAttachment(id, attachment)`. Usuń API `/cv`.
- Komponent wyboru plików (rozbudowa `cv-upload` → `attachments-picker`): wiele plików, dla każdego wybór rodzaju (select z etykietą), walidacja klienta (typy .pdf/.docx/.png/.jpg/.jpeg, ≤ 5 MB/plik, suma z istniejącymi ≤ 25 MB, ≤ 10 plików), lista wybranych z rozmiarem i przyciskiem usuń, licznik „Wykorzystano X z 25 MB”.
- WCAG (poprawki z review v1): po usunięciu pozycji fokus wraca do sensownego elementu (input/następna pozycja), jeden `role="status"` (sr-only) ogłaszający „Wysyłanie załączników…”, „Załączniki przesłane”, „Przerwano wysyłanie” (bez ogłaszania każdego procenta), `aria-disabled` zamiast `disabled` na przyciskach pobierania w trakcie pobierania (fokus nie ginie), etykiety pól rodzaju powiązane z nazwą pliku.
- Aplikowanie: wiele załączników w dialogu; najpierw POST aplikacji, potem jedno żądanie z plikami; komunikat przy porażce jak w v1 („Aplikacja złożona, ale nie udało się dołączyć załączników – możesz dodać je w Moich aplikacjach”), także gdy użytkownik opuści stronę w trakcie (snackbar z informacją).
- „Moje aplikacje”: lista załączników (rodzaj, nazwa, rozmiar, data), Dodaj (dialog z pickerem, w dozwolonych statusach), Usuń (zawsze), Pobierz; po zamknięciu dialogu bez wyniku (przerwanie w fazie przetwarzania) – odśwież aplikację z serwera.
- Zakładka aplikacji (admin/pracodawca) i dialog historii: lista załączników do pobrania (przyciski z `aria-label` „Pobierz {rodzaj}: {nazwa}”).
- `file-download.ts`: `revokeObjectURL` po ~30 s (nie 0).
- Tłumaczenia wszystkich nowych komunikatów i 429.
- Bez nowych ostrzeżeń budżetu, OnPush, wszystko w lazy chunkach.

## Dodatkowo (z raportów testów v1)
- Backend: globalna obsługa wyjątków (`UseExceptionHandler` + ProblemDetails) – nieobsłużony wyjątek → 500 `{message:"An unexpected error occurred"}` bez szczegółów, z logiem; w Development może zostać strona deweloperska, ale testy w Production mają widzieć `{message}`.
- Backend: limity nagłówków/liczby części multipart dostosowane do wielu plików (np. ≤ 10 plików + ≤ 10 pól `kinds`), nadal ciasne.
- Backend: `SanitizeFileName` – kolejność: usuń znaki niedozwolone/sterujące i spacje końcowe, POTEM kropki końcowe (`". . ."` nie może dać `". . .pdf"`).
- Backend: `LocalFileStorage.DeleteAsync` nie usuwa katalogu, do którego równolegle trwa zapis (lub ponawianie odporne na wyścig) – przy wielu plikach na aplikację ryzyko rośnie.
- Testy v1 do przeniesienia na nowy model (backend): `AddApplicationCvMigrationTests` → test migracji `AddApplicationAttachments`, `CvConcurrencyTests` → limity łączne/liczba pod blokadą, `CvApplicationDtoTests` → `attachments`, `CvScenario` → helpery załączników. `CvDocxValidationTests`, `CvFileNameTests`, `CvCleanupTests`, `CvStorageFailureTests` – przenieść niemal bez zmian. Frontend: specy `cv-upload`, `cv-dialog.integration`, metody serwisu `*Cv` → odpowiedniki dla załączników.
- Frontend: Escape w dialogu w trakcie wysyłania ANULUJE upload (z potwierdzeniem nie trzeba) i zamyka dialog; dialogi z `ariaModal: true`.

## Środowisko
Dodatkowy kontener deweloperski `jobboard-dev-seaweedfs` (SeaweedFS 4.50, S3 bez uwierzytelniania pod `http://jobboard-dev-seaweedfs:8333`). Konfiguracja z uwierzytelnianiem z `docker-compose.yml` została zweryfikowana przez koordynatora na tymczasowym kontenerze (healthz 200, anonim 403, poprawne klucze OK, złe odrzucone). Po restarcie: `docker start jobboard-dev-mysql jobboard-dev-sdk jobboard-dev-node jobboard-dev-seaweedfs`.

Kolejność: przebudowa v2 (backend + frontend) → testy → review → poprawki → merge (po merge'u gałęzi `chore/dependency-updates` do `main` i dopasowaniu) → usunięcie tego pliku.

## Frontend – stan v2

Zrobione (tylko `frontend/src/`, testy 1280/1280, `ng build` bez nowych ostrzeżeń budżetu):
- Model: `attachments: ApplicationAttachment[]` + `attachmentsTotalSize` zamiast pól `Cv*`; `AttachmentKind` (0–3), `AttachmentUploadItem { file, kind }`, `AttachmentUploadEvent`.
- Serwis: `uploadAttachments(id, items)` – jeden POST multipart (`files` + równoległe `kinds` jako liczby), `reportProgress`; `deleteAttachment(id, attachmentId)`; `downloadAttachment(id, { id, fileName })` (Blob, nazwa z `Content-Disposition` → `fileName` → `zalacznik`, treść błędu z Bloba parsowana do `{ message }`). API `/cv` usunięte.
- `utils/attachments.ts` (zamiast `cv-file.ts`): limity (5 MB / 25 MB / 10), `ATTACHMENT_ACCEPT` (.pdf,.docx,.png,.jpg,.jpeg), etykiety rodzajów (CV / List motywacyjny / Portfolio / Inny), `validateAttachmentFile`, `selectAttachmentFiles` (po kolei: typ, pusty, 5 MB, duplikat, liczba z istniejącymi, suma z istniejącymi; przyjmuje te, które się mieszczą, reszta z powodem i nazwą pliku), `defaultAttachmentKind` (CV, dopóki aplikacja go nie ma), `formatAttachmentsUsage`. `canAddAttachments` w `status-labels.ts` (Submitted–Offered). `file-download.ts`: `revokeObjectURL` po 30 s.
- `shared/attachments-picker.component.ts` (OnPush, `[(items)]`, `existing`, `progress`, `errorMessage`, `statusMessage`): `<input type="file" multiple>` ukryte wizualnie z etykietą-przyciskiem, lista wybranych (`<ul aria-label>`), dla każdego pliku natywny `<select>` z `<label>Rodzaj</label>` i `aria-labelledby="etykieta nazwa-pliku"`, usuń (`aria-label="Usuń z listy: …"`), licznik „Wykorzystano X z 25 MB · plików: N z 10” w `aria-describedby`, obszar podpowiedź/błąd (`role="alert"`)/pasek postępu o stałej wysokości, jeden `role="status"` (sr-only): „Dodano do listy…”, „Usunięto z listy…”, „Wysyłanie załączników…”, „Przetwarzanie załączników…”, komunikat końcowy od rodzica – bez kolejnych procentów. Fokus po usunięciu: następna pozycja → poprzednia → pole pliku (ustawiany przed usunięciem na element, który zostaje w DOM).
- `shared/attachment-list.component.ts` (OnPush): lista z pobieraniem (przycisk `aria-label="Pobierz {rodzaj}: {nazwa}"`, w trakcie `disabledInteractive` → `aria-disabled="true"` bez `disabled`, fokus zostaje) i opcjonalnym usuwaniem (confirm, `aria-disabled` w trakcie, 404 `Attachment not found` = już usunięty); błąd w `role="alert"`; po usunięciu fokus na sąsiednią pozycję albo na tekst pustej listy (`tabindex="-1"`); zniszczenie przerywa żądania.
- Dialog aplikowania (OnPush, `APPLY_DIALOG_CONFIG` z `ariaModal: true`): notatka + picker; najpierw POST aplikacji, potem jedno żądanie z plikami; postęp na przycisku, etap w sr-only `role="status"` strony („Wysyłanie załączników…”, „Załączniki przesłane”, „Przerwano wysyłanie”). Błąd albo opuszczenie strony w trakcie (także przed odpowiedzią na POST) → snackbar 8 s „Aplikacja złożona, ale nie udało się dołączyć załączników – możesz dodać je w Moich aplikacjach”.
- `my-applications/attachments-dialog.component.ts` (OnPush, `ATTACHMENTS_DIALOG_CONFIG` z `ariaModal: true`): `disableClose` + własna obsługa Escape/tła – Escape (także w fazie przetwarzania) i „Przerwij wysyłanie” od razu anulują żądanie i zamykają dialog z wynikiem `'aborted'`; kliknięcie w tło w trakcie wysyłania niczego nie przerywa. „Moje aplikacje”: kolumna „Załączniki” (rodzaj, nazwa, rozmiar, data; pobierz i usuń zawsze, „Dodaj” w dozwolonych statusach i poniżej 10 plików); po `'aborted'` aplikacja jest odświeżana z `GET /{id}` (także cache historii); tabela z `trackBy` po id (podmiana aplikacji nie odtwarza wiersza – fokus nie ginie).
- Zakładka aplikacji (admin/pracodawca): kolumna „Załączniki” z listą do pobrania; dialog historii (OnPush): sekcja „Załączniki” (aktualizowana z odpowiedzi szczegółów), żądanie historii anulowane przy zamknięciu; dialogi statusu i historii z `ariaModal: true`.
- Tłumaczenia: wszystkie nowe komunikaty z kontraktu, `Attachment not found`, `Too many requests - try again later` i status 429 bez treści („Zbyt wiele żądań – spróbuj ponownie za kilka minut”), 413 („Przesyłane pliki są za duże – maksymalnie 5 MB na plik i 25 MB łącznie”); 500 nadal ogólny komunikat.

Niejasności i rozstrzygnięcia:
- Komunikaty „Załączniki przesłane”/„Przerwano wysyłanie” w dialogu „Moich aplikacji”: dialog zamyka się od razu, więc sukces i przerwanie ogłasza snackbar strony („Załączniki zostały dodane do aplikacji” / „Przerwano wysyłanie załączników”); w dialogu po błędzie API region `status` mówi „Przerwano wysyłanie załączników”, a powód jest w `role="alert"`.
- Zamknięcie dialogu programowo (`closeAll`, nawigacja) przerywa żądanie, ale daje wynik `undefined` (bez odświeżenia – strona i tak znika).
- Rodzaj wybierany natywnym `<select>` (lekki, w pełni dostępny z klawiatury, bez overlayu w dialogu); ramka #8a8a8a dla kontrastu ≥ 3:1.
- Błędy pobierania/usuwania pokazywane w liście (`role="alert"`) zamiast `alert()` w zakładce admina – spójnie w trzech miejscach.
- Paczka startowa: 623,41 kB (+0,27 kB względem 623,14 kB) – kod bibliotek we wspólnym chunku startowym (CDK/Material/core); nowe komponenty są w leniwych chunkach, nowych ostrzeżeń brak.

Ryzyka / do sprawdzenia z działającym backendem:
- Smoke test: kolejność `kinds` vs `files` w multipart, 201 z listą, 25 MB na granicy (nginx 26m), 429 z polityki `uploads`.

## Backend – stan v2

Zrealizowane wg kontraktu (gałąź `feature/cv-upload`):
- Model: `AttachmentKind`, `ApplicationAttachment` (tabela `ApplicationAttachments`, FK z kaskadą, unikalny `FileKey`, indeks po `JobApplicationId`). Migracja `AddApplicationCv` cofnięta na dev `JobBoardDb` i usunięta, w jej miejsce `20261106103645_AddApplicationAttachments` (zaaplikowana na `JobBoardDb`; Up/Down/Up sprawdzone testami na bazie z danymi). Kolumn `Cv*` i endpointów `/cv` już nie ma.
- `POST /api/jobapplications/{id}/attachments`: lekkie zapytanie o właściciela i status (`GetOwnershipAsync`) przed odczytem ciała; `[DisableFormValueModelBinding]` – bez niego MVC wczytywał cały formularz już przy wiązaniu `id` (przed autoryzacją). Formularz czytany ręcznie w akcji z własnymi limitami (Kestrel `MaxRequestBodySize` 26 MB + strumień z limitem 26 MB niezależny od serwera, `Content-Length` > 26 MB → 400 bez czytania, max 11 plików / 11 pól tekstowych, nagłówki części 4 KB). Walidacja wszystkich plików → zapis plików → rekordy w transakcji z blokadą X wiersza aplikacji (UPDATE bez zmian, jak w etapie 1), w której liczone są istniejące załączniki (10 plików / 25 MB) i ponownie sprawdzany status; każdy błąd lub odrzucenie usuwa zapisane pliki. Odpowiedź 201 z `Location` na `GET /api/jobapplications/{id}`.
- `kinds`: brak/za mało pól → `Other`; wartość spoza enuma (także `" 1"`, `"1.0"`) albo więcej pól niż plików → 400 `Invalid attachment kind` (komunikat spoza listy kontraktu – frontend może go potrzebować w tłumaczeniach). Plik w innym polu niż `files` → 400 `No file uploaded...`.
- Żądanie > 26 MB (po `Content-Length` albo po przekroczeniu limitu w trakcie czytania) → 400 `Total size of attachments cannot exceed 25 MB`.
- `DELETE .../attachments/{attachmentId}` – tylko właściciel, każdy status, plik po usunięciu rekordu; `GET .../attachments/{attachmentId}` – `Content-Length` z magazynu (Local – plik, S3 – nagłówek odpowiedzi; `IFileStorage.OpenReadAsync` zwraca `StoredFile(Content, Length)`), strumień zwalniany także przy wyjątku.
- Rate limiting: polityka `uploads`, okno stałe 30 żądań / 600 s na użytkownika (`RateLimiting:Uploads:PermitLimit`, `WindowSeconds`, walidowane przy starcie), 429 `{message:"Too many requests - try again later"}` + `Retry-After`. Okno stałe zamiast przesuwnego, bo tylko ono podaje `Retry-After`. Limiter po `UseAuthorization` (anonim dostaje 401 bez liczenia); liczą się też żądania na cudze id.
- Globalna obsługa wyjątków: `AddProblemDetails` + `AddExceptionHandler<GlobalExceptionHandler>` + `UseExceptionHandler()` jako pierwszy middleware – 500 `{message:"An unexpected error occurred"}` we wszystkich środowiskach (także Development), wyjątek loguje middleware.
- DOCX: przed `new ZipArchive` rekord EOCD – jeden dysk, bez ZIP64, 1–1000 wpisów, katalog ≤ 256 KB, ≥ 46 B na wpis, w całości przed EOCD; potem limity wpisów i 50 MB po rozpakowaniu. PNG/JPEG po sygnaturach (`.png`, `.jpg`).
- `SanitizeFileName`: najpierw usuwa znaki sterujące/formatujące i zamienia niedozwolone, potem razem kropki i spacje końcowe; nazwy zarezerwowane Windows (część przed pierwszą kropką) → prefiks `_`; nazwa awaryjna `attachment`.
- `LocalFileStorage`: plik tymczasowy `{nazwa}.{guid}.tmp` w katalogu docelowym, `Flush(true)` przed `Move`, usuwany przy błędzie (z pustym katalogiem); `*.tmp` starsze niż 1 h usuwane w `InitializeAsync`; licznik zapisów w toku per katalog – usuwanie pustych katalogów pomija katalogi z trwającym zapisem (+ ponawianie przy innym procesie).
- `DeleteQuietlyAsync` – 10 s na plik; reset danych demo (`DeleteByPrefixAsync("attachments/")`) – 2 min.
- Infrastruktura: `Backend/.dockerignore` += `storage/`, nginx `client_max_body_size 26m` w `location /api/`, `docker-up.ps1` wymienia klucze S3 niepasujące do `^[A-Za-z0-9]{16,}$` (z ostrzeżeniem, BOM zachowany), README i `.env.example` zaktualizowane.

Testy: wszystkie testy CV przeniesione na załączniki (`Attachment*Tests`, `AddApplicationAttachmentsMigrationTests`, `AttachmentFilesTests`, `AttachmentScenario`/`AttachmentTestFiles`) + nowe: limity pod współbieżnością (bariery na repozytorium i prawdziwe równoległe żądania), wszystko albo nic, DELETE w każdym statusie, kontrola właściciela przed odczytem ciała (licznik bajtów odczytanych przez aplikację), EOCD, PNG/JPEG, ucięty multipart, rate limit (`AttachmentRateLimitTests`, domyślna konfiguracja), `GlobalExceptionHandlerTests` (Production i Development). Fabryka testowa podnosi limit uploadów.
Wyniki: `dotnet build` 0 ostrzeżeń; pełny `dotnet test` – 1306 zaliczonych, 6 pominiętych (S3 bez zmiennej); `S3FileStorageTests` z `JOBBOARD_TEST_S3_URL=http://jobboard-dev-seaweedfs:8333` – 6/6. Smoke test (Development, port 5099, Local i S3 na SeaweedFS): 3 załączniki w jednym żądaniu (201) → 6 MB, suma > 25 MB, 11. plik, GIF – 400 i magazyn bez zmian → pracodawca firmy pobiera każdy plik (Content-Type, Content-Length zgodny z treścią, Content-Disposition `filename*`, nosniff, no-store) → pracodawca innej firmy 404 → odrzucenie, kandydat usuwa załącznik (204) → admin usuwa aplikację, magazyn pusty → 31. żądanie uploadu 429.

Ryzyka:
- Bazy, na których kiedyś zaaplikowano usuniętą `AddApplicationCv` (tylko z tej gałęzi), zostają z pustymi kolumnami `Cv*` i wpisem w `__EFMigrationsHistory` – nieszkodliwe (kolumny nullable), ale do ręcznego sprzątnięcia.
- Limit 26 MB niezależny od serwera działa na poziomie strumienia; przy braku `Content-Length` klient dostaje błąd dopiero po wysłaniu 26 MB. Kestrel bez `Content-Length` zrywa połączenie po przekroczeniu (przeglądarka może nie pokazać JSON-a – frontend waliduje rozmiar wcześniej).
- Rate limiter trzyma stan w pamięci procesu (przy kilku instancjach backendu limit jest per instancja).
