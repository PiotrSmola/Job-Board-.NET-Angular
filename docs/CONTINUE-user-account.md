# Etap 4 – Konto użytkownika, e-maile, zapisane oferty (feature/user-account) – KONTRAKT

Frontend powstaje PRZED backendem (równolegle z innymi pracami) – ten kontrakt jest źródłem prawdy dla obu stron. JSON camelCase, enumy jako liczby, błędy `{ message }` lub ValidationProblem (`errors`), paginacja jak w reszcie API (nagłówek `Pagination`).

## E. Profil i bezpieczeństwo konta (`/api/account`, każdy zalogowany)
- `GET /api/account/profile` → `ProfileDto { email, firstName, lastName, phoneNumber, headline, location, linkedInUrl, about, emailConfirmed, roles: string[], createdAt }`.
- `PUT /api/account/profile` body `{ firstName (≤100, wymagane), lastName (≤100, wymagane), phoneNumber? (≤30, cyfry/spacje/+/-), headline? (≤150), location? (≤200), linkedInUrl? (≤300, https://(www.)linkedin.com/...), about? (≤2000) }` → 200 `ProfileDto`. Email nie jest zmieniany tym endpointem.
- `POST /api/account/change-password` body `{ currentPassword, newPassword }` → 200 `UserDto` (NOWY token; stare tokeny unieważnione przez zmianę security stamp). Złe obecne hasło → 400 `{message:"Current password is incorrect"}`; słabe nowe → 400 ValidationProblem z błędami Identity.
- `DELETE /api/account` body `{ password }` → 204 (usuwa konto, aplikacje i załączniki kandydata, dokumenty profilu; pliki w magazynie). Złe hasło → 400 `Current password is incorrect`. Admin → 400 `{message:"Administrator accounts cannot be deleted here"}`. Ostatni członek firmy z ofertami → 400 `{message:"You are the last member of a company with job offers"}`.
- Dokumenty profilu (domyślne CV itp., tylko kandydat): `GET /api/account/documents` → `[{ id, kind (AttachmentKind 0–3), fileName, size, uploadedAt }]`; `POST /api/account/documents` multipart (`files` + `kinds`, jak załączniki aplikacji; limity: 5 MB/plik, 25 MB i 10 plików łącznie na profil) → 201 lista; `DELETE /api/account/documents/{id}` → 204; `GET /api/account/documents/{id}` → plik (nagłówki jak przy załącznikach).
- Dołączenie dokumentów z profilu do aplikacji: `POST /api/jobapplications/{id}/attachments/from-profile` body `{ documentIds: number[] }` → 201 `JobApplicationDto` (kopie plików; limity aplikacji 25 MB / 10 plików jak przy uploadzie; te same reguły statusów i właściciela).

## F. E-maile (potwierdzenie, reset hasła, powiadomienia)
- Rejestracja wysyła e-mail z linkiem potwierdzającym `<FRONTEND_URL>/confirm-email?userId=...&token=...` (token URL-encoded). Niepotwierdzony e-mail NIE blokuje korzystania z aplikacji (tylko baner).
- `POST /api/auth/confirm-email` body `{ userId, token }` → 204; zły/wygasły → 400 `{message:"Invalid or expired confirmation link"}`.
- `POST /api/auth/resend-confirmation` [zalogowany] → 204 (rate limit; już potwierdzony → 400 `{message:"Email is already confirmed"}`).
- `POST /api/auth/forgot-password` body `{ email }` → ZAWSZE 202 (bez ujawniania istnienia konta; rate limit per IP). E-mail z linkiem `<FRONTEND_URL>/reset-password?email=...&token=...` (ważny 1 h).
- `POST /api/auth/reset-password` body `{ email, token, newPassword }` → 204; zły/wygasły token → 400 `{message:"Invalid or expired reset link"}`; słabe hasło → 400 ValidationProblem. Reset unieważnia stare tokeny.
- Powiadomienia e-mail (wysyłane w tle, z ponawianiem): kandydat – potwierdzenie złożenia aplikacji, zmiana statusu aplikacji (z komentarzem rekrutera); pracodawca (członkowie firmy) – nowa aplikacja do ich oferty, decyzja admina o firmie (zatwierdzona/odrzucona z powodem).
- Preferencje: `GET /api/account/notifications` → `{ applicationStatusChanged, newApplication, companyReview }` (bool, domyślnie true; pola nieistotne dla roli mogą być ignorowane w UI); `PUT /api/account/notifications` (to samo body) → 200. E-maile bezpieczeństwa (reset, potwierdzenie) zawsze wysyłane.
- Lokalnie: Mailpit w docker-compose (UI na wolnym porcie wybieranym przez `docker-up.ps1`, tylko 127.0.0.1).

## G. Zapisane oferty (tylko kandydat)
- `PUT /api/saved-jobs/{jobId}` → 204 (idempotentne); oferta niewidoczna publicznie → 404.
- `DELETE /api/saved-jobs/{jobId}` → 204 (idempotentne).
- `GET /api/saved-jobs` (paginacja `pageNumber`, `pageSize`) → `[{ savedAt, job: JobOfferDto }]` – także oferty, które w międzyczasie wygasły/zamknięto (JobOfferDto ma `status`, `isExpired`), posortowane od najnowszych zapisów.
- `GET /api/saved-jobs/ids` → `number[]` (do oznaczania serduszek na liście bez N+1).
- Pracodawca/admin → 403 na wszystkich.

## Frontend
- Trasy (lazy): `/account` (guard zalogowany) z zakładkami: Profil, Dokumenty (tylko kandydat), Bezpieczeństwo (zmiana hasła, usunięcie konta z potwierdzeniem hasłem), Powiadomienia; `/forgot-password`, `/reset-password`, `/confirm-email` (publiczne); `/saved-jobs` (candidateGuard).
- Navbar/menu użytkownika: „Moje konto”, „Zapisane oferty” (kandydat); link „Nie pamiętasz hasła?” na stronie logowania.
- Baner „Potwierdź adres e-mail” (z przyciskiem wyślij ponownie) dla zalogowanych z `emailConfirmed=false` – nie przesuwa układu (CLS).
- Zmiana hasła → zapisanie nowego tokena z odpowiedzi (sesja trwa). Usunięcie konta → wylogowanie i komunikat.
- Zapisane oferty: przycisk-przełącznik (serduszko, `aria-pressed`, `aria-label` „Zapisz ofertę: {tytuł}” / „Usuń z zapisanych: {tytuł}”) na liście i w szczegółach oferty; optymistyczna aktualizacja z cofnięciem przy błędzie; stan z `GET /api/saved-jobs/ids` ładowany raz po zalogowaniu (serwis z sygnałem).
- Dialog aplikowania: sekcja „Dołącz z profilu” (checkboxy dokumentów profilu) obok uploadu nowych plików (suma limitów liczona łącznie).
- Wszystkie formularze: walidacja klienta zgodna z backendem, błędy ogłaszane, `autocomplete` (`current-password`, `new-password`, `email`, `given-name`...), wskaźnik siły hasła tekstowo (nie tylko kolorem).

## Stan prac

Frontend jest robiony PRZED backendem (równolegle z etapem 3 i aktualizacjami), w worktree `.worktrees/user-account`, od commita `a421eff` gałęzi `feature/cv-upload`. Backend powstanie później wg tego kontraktu (po merge'u aktualizacji zależności, etapu 3 i przeglądu jakości do `main`). Potem: testy, review, poprawki, dopasowanie do nowej wersji Angulara, merge.
