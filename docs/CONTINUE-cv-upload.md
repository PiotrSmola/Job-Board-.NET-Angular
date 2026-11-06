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
