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
