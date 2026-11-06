# Etap 3 – CV przy aplikowaniu (feature/cv-upload) – KONTRAKT

Cel: kandydat dołącza CV do aplikacji (przy aplikowaniu lub później), rekruter (admin / pracodawca firmy oferty) pobiera je. Pliki w magazynie zgodnym z S3 (SeaweedFS) – NIE oficjalne MinIO.

## Magazyn plików (backend)
- Interfejs `IFileStorage { Task SaveAsync(string key, Stream content, string contentType, CancellationToken); Task<Stream?> OpenReadAsync(string key, CancellationToken); Task DeleteAsync(string key, CancellationToken); }` (+ ewentualnie `DeleteByPrefixAsync` dla resetu seeda).
- Implementacje: `LocalFileStorage` (katalog z konfiguracji, domyślny w Development i w testach) oraz `S3FileStorage` (pakiet `AWSSDK.S3`, `ForcePathStyle = true`, `ServiceURL`, bucket tworzony przy starcie jeśli nie istnieje).
- Konfiguracja `Storage`: `Provider` (`Local` | `S3`, domyślnie `Local`), `LocalPath` (domyślnie `<ContentRoot>/storage`), `S3:ServiceUrl`, `S3:Bucket` (domyślnie `jobboard`), `S3:AccessKey`, `S3:SecretKey`. Walidacja konfiguracji przy starcie (S3 bez URL/kluczy → czytelny wyjątek).
- Klucze plików generowane przez serwer: `cv/{applicationId}/{guid}{ext}` – NIGDY z nazwy pliku użytkownika. Katalog `storage/` w `.gitignore`.

## Model
- `JobApplication` += `CvFileKey` (max 300, nullable), `CvFileName` (oryginalna nazwa po sanityzacji, max 255), `CvContentType` (max 100), `CvSize` (long?), `CvUploadedAt` (DateTime?, UTC). Migracja (kolumny nullable, bez backfillu).
- `JobApplicationDto` += `hasCv` (bool), `cvFileName`, `cvSize`, `cvUploadedAt` (null gdy brak). Klucz pliku NIGDY nie trafia do DTO.

## Walidacja pliku
- Dozwolone: PDF (`%PDF-` na początku) oraz DOCX (ZIP `PK\x03\x04` zawierający `[Content_Types].xml` i `word/`). Typ wykrywany po zawartości (magic bytes), nie po nagłówku/rozszerzeniu klienta; content type ustalany przez serwer (`application/pdf` / `application/vnd.openxmlformats-officedocument.wordprocessingml.document`).
- Maks. 5 MB (`[RequestSizeLimit]` ~6 MB na endpoint + sprawdzenie długości pliku); pusty plik → 400.
- Sanityzacja nazwy (usunięcie ścieżek, znaków kontrolnych, max 255, rozszerzenie zgodne z wykrytym typem).
- Błędy: 400 `{message:"Unsupported file type - upload a PDF or DOCX file"}`, 400 `{message:"File is too large - maximum size is 5 MB"}`, 400 `{message:"File is empty"}`.

## Endpointy
- `PUT /api/jobapplications/{id}/cv` (multipart/form-data, pole `file`) – tylko właściciel aplikacji; dozwolone gdy status ∈ {Submitted, InReview, Interview, Offered}; inaczej 400 `{message:"CV cannot be changed in the current application status"}`. Zastępuje poprzedni plik (stary usuwany z magazynu PO zapisie nowego i commicie). → 200 `JobApplicationDto`. Cudza/nieistniejąca → 404.
- `DELETE /api/jobapplications/{id}/cv` – właściciel, te same statusy → 204; brak CV → 404.
- `GET /api/jobapplications/{id}/cv` – właściciel, admin, pracodawca firmy oferty (przez `ICurrentUserAccess`); inaczej 404. Odpowiedź: strumień pliku, `Content-Type` z bazy, `Content-Disposition: attachment; filename*=UTF-8''...`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`. Brak CV / brak pliku w magazynie → 404.
- Sprzątanie: usunięcie aplikacji przez admina, usunięcie oferty (admin) i RefreshSeed usuwają pliki CV powiązanych aplikacji (po commicie transakcji; błąd usuwania pliku tylko logowany). Reaktywacja wycofanej aplikacji zachowuje CV.

## docker-compose (lokalny stack)
- Nowa usługa `storage`: obraz `chrislusf/seaweedfs:4.50`, `server -s3 -dir=/data`, wolumen `storage-data`, BEZ publikowania portów (dostęp tylko z sieci compose). Uwierzytelnianie S3 włączone: plik konfiguracyjny S3 SeaweedFS z tożsamością o kluczach z `.env` (`S3_ACCESS_KEY`, `S3_SECRET_KEY`) – generowany przy starcie kontenera (entrypoint `sh -c` zapisujący JSON z env, potem `exec weed ...`) albo inna sprawdzona metoda; healthcheck.
- Backend: `Storage__Provider: S3`, `Storage__S3__ServiceUrl: http://storage:8333`, klucze z env; `depends_on` storage (healthy).
- `docker-up.ps1`: generuje i zachowuje `S3_ACCESS_KEY` (20 znaków) i `S3_SECRET_KEY` (40 znaków) jak `JWT_SECRET` (BOM zachowany); `.env.example` uzupełniony.

## Frontend
- Model `JobApplication` += `hasCv`, `cvFileName`, `cvSize`, `cvUploadedAt`. Serwis: `uploadCv(id, file)` (FormData, PUT), `deleteCv(id)`, `downloadCv(id)` (GET jako `blob`, zapis pliku przez `URL.createObjectURL` + link z nazwą z `Content-Disposition` lub `cvFileName`; pamiętaj o `revokeObjectURL`). Nagłówek `Content-Disposition` musi być wystawiony przez CORS (`WithExposedHeaders`) – backend to dodaje.
- Wspólny komponent wyboru pliku (`components/shared/cv-upload`): akceptuje `.pdf,.docx`, walidacja po stronie klienta (rozszerzenie, ≤5 MB), pokazuje nazwę i rozmiar, przycisk usuń; komunikaty po polsku.
- Dialog aplikowania (job-detail): opcjonalne CV; najpierw `POST /api/jobapplications`, potem `PUT .../cv`; gdy upload się nie powiedzie – aplikacja zostaje złożona, komunikat „Aplikacja złożona, ale nie udało się dołączyć CV – możesz dodać je w Moich aplikacjach”.
- „Moje aplikacje”: informacja o CV (nazwa, rozmiar, data), akcje Dodaj/Zmień/Usuń/Pobierz zgodnie z dozwolonymi statusami.
- Zakładka aplikacji (admin i pracodawca): ikona/przycisk „Pobierz CV” gdy `hasCv`; w dialogu historii też.
- Tłumaczenia nowych komunikatów API w `getApiErrorMessage`.
- Budżety: bez nowych ostrzeżeń.

## Środowisko

Dodatkowy kontener deweloperski `jobboard-dev-seaweedfs` (SeaweedFS 4.50, S3 bez uwierzytelniania pod `http://jobboard-dev-seaweedfs:8333` w sieci `jobboard-dev`). Uruchomienie po restarcie: `docker start jobboard-dev-mysql jobboard-dev-sdk jobboard-dev-node jobboard-dev-seaweedfs`.

Kolejność pracy: implementacja (backend + frontend) → testy → review → poprawki → merge do `main`, potem usunięcie tego pliku.

## Frontend – stan

Zrobione (gałąź `feature/cv-upload`, tylko `frontend/`):
- Model `JobApplication` += `hasCv`, `cvFileName`, `cvSize`, `cvUploadedAt` (wymagane – DTO zawsze je zwraca); typ `CvUploadEvent` (`progress` z procentem | `done` z aplikacją).
- `JobApplicationService`: `uploadCv(id, file)` (FormData, pole `file`, `reportProgress` + `observe: 'events'`, mapowane na `CvUploadEvent`), `deleteCv(id)`, `downloadCv(id, fallbackFileName)` (`responseType: 'blob'`, `observe: 'response'`; treść błędu – Blob z JSON – parsowana do `{ message }`, żeby działał `getApiErrorMessage`).
- Utilsy: `utils/content-disposition.ts` (`filename*=` UTF-8 / ISO-8859-1 → `filename=` → `cvFileName` → `cv`; usuwa ścieżki, znaki sterujące, a `<>:"|?*` zamienia na `_`), `utils/file-download.ts` (link `blob:` tworzony programowo, bez `target`, `revokeObjectURL` w kolejnym takcie), `utils/blob-error.ts` (FileReader – działa też w jsdom), `utils/cv-file.ts` (walidacja: rozszerzenie `.pdf/.docx`, ≤ 5 MB, niepusty; `formatFileSize`), `canChangeCv` w `status-labels.ts` (Submitted/InReview/Interview/Offered).
- `components/shared/cv-upload.component.ts` (OnPush): prawdziwe `<input type="file">` ukryte wizualnie z `<label>` w roli przycisku (widoczny fokus), `aria-describedby` na stały obszar statusu (podpowiedź / błąd z `role="alert"` / `mat-progress-bar` z `aria-valuetext`) – miejsce zarezerwowane, bez przesunięć układu; `[(file)]` jako input + output `fileChange` (bez `model()`, żeby nie powiększać paczki startowej); `errorMessage` na błąd z API.
- Dialog aplikowania: opcjonalne CV; najpierw POST, potem PUT; postęp na przycisku („Wysyłanie CV… X%” / „Przetwarzanie CV…”); błąd uploadu → komunikat z kontraktu (8 s). Opuszczenie strony przerywa upload (`takeUntilDestroyed`).
- „Moje aplikacje”: kolumna CV (nazwa → pobranie, rozmiar, data), przyciski Dodaj/Zmień (dialog `my-applications/cv-dialog.component.ts` z paskiem postępu; zamknięcie przerywa upload) i Usuń (confirm) tylko w statusach z `canChangeCv`; pobieranie zawsze, gdy `hasCv`.
- Zakładka aplikacji (admin i pracodawca): przycisk „Pobierz CV: nazwa” przy `hasCv`; dialog historii: informacja o CV + „Pobierz CV”.
- Tłumaczenia: komunikaty walidacji pliku, `Invalid multipart form data`, `CV cannot be changed…`, `CV not found`, `CV was changed by another request`, `No file uploaded…`; status 413 → „Plik jest za duży…”.
- Paczka startowa bez zmian (623,1 kB jak na `main`); interceptor nietknięty.

Niejasności i rozstrzygnięcia:
- Parsowanie treści błędu Blob jest w `downloadCv` (leniwy chunk), nie w interceptorze – inaczej rośnie paczka startowa. Skutek: dla 403 przy pobieraniu interceptor pokaże ogólne „Brak uprawnień” (kontrakt i tak zwraca 404 dla braku dostępu).
- Błąd uploadu CV po złożeniu aplikacji pokazuje komunikat z kontraktu także przy 401/403 (aplikacja jest złożona – to ważniejsza informacja).
- Postęp przy aplikowaniu jest na przycisku strony oferty (dialog zamyka się przed POST, jak dotychczas); pełny pasek postępu jest w dialogu CV w „Moich aplikacjach”.

Kolejne kroki:
- Smoke test z działającym backendem (CORS `Content-Disposition`, nazwy z polskimi znakami, 5 MB, DOCX).
- Pełniejsze testy (osobny agent): np. e2e upload/pobranie, kontrast w motywie, nawigacja klawiaturą w dialogach.

## Backend – stan

Zrobione (gałąź `feature/cv-upload`: `Backend/`, `Backend.Tests/`, `docker-compose.yml`, `docker-up.ps1`, `.env.example`, `.gitignore`, `README.md`):
- `IFileStorage` (+ `InitializeAsync` – katalog / bucket przy starcie, `DeleteByPrefixAsync` – reset seeda), `LocalFileStorage` (zapis przez plik tymczasowy + podmiana; klucze z `..`, `\`, `:`, znakami sterującymi, absolutne i wychodzące poza katalog po `GetFullPath` są odrzucane), `S3FileStorage` (`AWSSDK.S3` 4.x, singleton, `ForcePathStyle`, sumy CRC tylko gdy wymagane, PUT bez aws-chunked, pobieranie zwalnia `GetObjectResponse` razem ze strumieniem). `StorageSettings` walidowane przy starcie (S3 bez URL/kluczy albo nieznany provider → `InvalidOperationException`).
- Migracja `AddApplicationCv` (5 kolumn nullable). DTO: `hasCv`, `cvFileName`, `cvSize`, `cvUploadedAt` (null bez CV); klucza brak w API.
- Endpointy dokładnie wg kontraktu; CORS wystawia `Content-Disposition`; sprzątanie plików po usunięciu aplikacji / oferty (admin) i po RefreshSeed (`DeleteByPrefixAsync("cv/")`).
- docker-compose: usługa `storage` (SeaweedFS 4.50, bez portów, `-s3.config` generowany przy starcie, healthcheck `/healthz`), backend `Storage__*` + `depends_on: storage (healthy)`, wolumen `storage-data`; `docker-up.ps1` generuje/zachowuje `S3_ACCESS_KEY` (20) i `S3_SECRET_KEY` (40).

Dodatki i odstępstwa względem kontraktu (frontend ma już tłumaczenia):
- `409 {message:"CV was changed by another request"}` – zamiana/usunięcie CV jest warunkowym UPDATE z tokenem „bieżący klucz pliku” (+ właściciel + dozwolony status); równoległa zmiana CV daje 409, równoległa decyzja końcowa – 400 z kontraktu, usunięta aplikacja – 404.
- `400 {message:"No file uploaded - send it in the 'file' form field"}` – brak pola `file`; `400 {message:"Invalid multipart form data"}` – uszkodzony multipart / przekroczone limity formularza.
- `404 {message:"CV not found"}` – brak CV albo brak pliku w magazynie (cudza/nieistniejąca aplikacja – `Application not found`).
- `DELETE .../cv` bez CV → 404 sprawdzane przed statusem (aplikacja odrzucona bez CV → 404, z CV → 400).
- Żądanie > 6 MB (Content-Length) → od razu 400 „File is too large…” (bez czytania treści); 5 MB = 5 242 880 B.
- `Content-Disposition` zawiera wyłącznie `filename*=UTF-8''…` (bez `filename=`), budowany przez `ContentDispositionHeaderValue`.
- DOCX: tylko katalog centralny ZIP, maks. 1000 wpisów (sprawdzane najpierw z rekordu EOCD) i 50 MB zadeklarowanej treści – inaczej „Unsupported file type…”.

Decyzje:
- Upload przez `IFormFile` wczytany w filtrze `[CvUploadForm]` (limit Kestrel 6 MB, limity `FormOptions` tylko dla tego endpointu); ASP.NET Core buforuje plik > 64 KB na dysku, strumień jest przewijalny – typ sprawdzany na nim i ten sam strumień idzie do magazynu. Bez `MemoryStream` także dla DOCX (ZipArchive czyta tylko katalog centralny z przewijalnego strumienia).
- Kolejność zamiany: zapis nowego pliku (token żądania) → UPDATE (bez tokenu – po zapisaniu pliku operacja jest doprowadzana do końca) → usunięcie starego pliku; błąd UPDATE → usunięcie nowego pliku. Usunięcie aplikacji/oferty: transakcja z blokadą wierszy (oferta, potem aplikacje) przed odczytem kluczy, pliki po commicie – równoległa zamiana CV nie osieroci pliku.
- Testy: magazyn `Local` w katalogu tymczasowym per fabryka; `S3FileStorageTests` tylko z `JOBBOARD_TEST_S3_URL` (inaczej Skip).

Ryzyka / do sprawdzenia:
- Konfiguracja S3 SeaweedFS w compose (format `identities`/`credentials`/`actions`, flaga `-s3.config`, `/healthz`, entrypoint obrazu z `su-exec`) zweryfikowana z dokumentacją i kodem źródłowym 4.50, ale nie uruchomiona (kontener deweloperski działa bez uwierzytelniania) – sprawdzić `.\docker-up.ps1` na czystym wolumenie.
- Osierocone pliki są możliwe tylko przy awarii magazynu po commicie (błąd usuwania jest logowany) albo przerwaniu procesu między zapisem pliku a UPDATE.

Weryfikacja: `dotnet build` 0 ostrzeżeń; `dotnet test` – 1019 testów (1015 zaliczonych, 4 testy S3 pominięte bez `JOBBOARD_TEST_S3_URL`; z `JOBBOARD_TEST_S3_URL=http://jobboard-dev-seaweedfs:8333` – 4/4 zaliczone). Smoke test (Development, port 5099) dla `Local` i `S3` (jobboard-dev-seaweedfs): upload PDF, PNG jako .pdf → 400, 5 MB + 1 B → 400, 7 MB (także chunked) → 400, pobranie przez pracodawcę firmy (nagłówki i CORS `Content-Disposition`), inny pracodawca → 404, zamiana na DOCX (stary plik usunięty z magazynu), usunięcie CV → 404, usunięcie aplikacji przez admina usuwa plik – wszystko OK.
