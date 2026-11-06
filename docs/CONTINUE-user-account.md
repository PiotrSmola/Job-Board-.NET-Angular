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

## Frontend – stan

Zrobione (na kontrakcie, bez backendu): modele `models/account.model.ts` (+ `ResetPasswordDto`, `ConfirmEmailDto`, `UserDto.emailConfirmed`), `AccountService`, `SavedJobsService`, rozszerzony `AuthService`, trasy lazy `/account` (authGuard; zakładki Profil / Dokumenty – tylko kandydat / Bezpieczeństwo / Powiadomienia, `?tab=documents|security|notifications`), `/forgot-password`, `/reset-password`, `/confirm-email` (publiczne), `/saved-jobs` (candidateGuard), baner potwierdzenia e-maila w powłoce, serduszka na liście i w szczegółach oferty, „Dołącz z profilu” w dialogu aplikowania, link „Nie pamiętasz hasła?”, menu „Moje konto” / „Zapisane oferty”, tłumaczenia komunikatów w `getApiErrorMessage`.

### Rozstrzygnięcia niejasności kontraktu (backend ma się do nich stosować)
1. **`UserDto.emailConfirmed: bool`** – nowe pole w odpowiedziach `login`, `register`, `register-employer` i `change-password`. Baner pokazuje się tylko przy jawnym `false` (brak pola = stara sesja = brak banera). Po `PUT /api/account/profile` front synchronizuje `firstName`, `lastName`, `emailConfirmed` z `ProfileDto` do sesji.
2. **Id użytkownika w JWT** – po `confirm-email` front porównuje `userId` z linku z claimem `nameid` (`ClaimTypes.NameIdentifier` po mapowaniu `JwtSecurityTokenHandler`; obsługiwane też `sub` i pełny URI) i tylko wtedy chowa baner.
3. **Tokeny w linkach** muszą być zakodowane `Uri.EscapeDataString` (albo Base64Url – zalecane `WebEncoders.Base64UrlEncode`), bo router Angulara zamienia surowy `+` w query na spację. Front odrzuca (bez wywołania API) linki bez `userId`/`token` lub `email`/`token`, e-mail bez `@`, token > 2048 znaków, userId > 128 znaków. Parametry są usuwane z URL (`replaceUrl`) zaraz po odczycie; strony `/reset-password`, `/confirm-email`, `/forgot-password` nie są akceptowane jako `returnUrl`.
4. **Potwierdzenie e-maila** wysyłane automatycznie po wejściu na `/confirm-email` (POST, nie GET – skanery linków w poczcie go nie wyzwolą). Odpowiedź 400 `Email is already confirmed` na `confirm-email` (gdyby backend ją zwracał) front traktuje jak sukces.
5. **Hasła** – walidacja klienta jak Identity w `Program.cs` (min. 6, mała i wielka litera, cyfra) + **maks. 100 znaków** (backend: `[MaxLength(100)]` na `newPassword`/`currentPassword`/`password`). Słabe hasło → ValidationProblem; front tłumaczy domyślne opisy `IdentityErrorDescriber` ("Passwords must …") z dowolnego klucza `errors` (zalecany klucz `NewPassword`) – nie zmieniać ich treści.
6. **Wynik `reset-password`**: jeśli zalogowany w przeglądarce jest ten sam e-mail, front czyści lokalną sesję (stary token i tak jest unieważniony). Po sukcesie: komunikat i przejście na `/login`.
7. **`DELETE /api/account`** – body `{ password }` (JSON, `Content-Type: application/json` – Angular wysyła body w DELETE). Front nie pokazuje formularza adminowi; po 204 czyści sesję, pokazuje „Konto zostało usunięte…” i przechodzi na `/jobs`.
8. **Profil – formaty**: `phoneNumber` `^[0-9+\- ]+$`; `linkedInUrl` `^https://(www\.)?linkedin\.com/\S+$` (bez rozróżniania wielkości liter, wymagana ścieżka po `/`). Puste pola opcjonalne front wysyła jako `null`, wartości przycięte (`trim`). `firstName`/`lastName` nie mogą być samymi spacjami.
9. **Dokumenty profilu**: `POST /api/account/documents` → 201 z **pełną listą dokumentów profilu** (front łączy po `id`, więc lista samych nowych też zadziała). Sortowanie jak załączniki (rodzaj, data). Komunikaty błędów do użycia przez API (tłumaczone na froncie): `A profile can have at most 10 documents`, `Total size of profile documents cannot exceed 25 MB`, `Document not found` (404 przy usuwaniu traktowane jak już usunięte), a dla typu/rozmiaru/pustego pliku/multipart – te same co przy załącznikach aplikacji.
10. **`from-profile`**: front najpierw składa aplikację, potem wysyła `from-profile`, a na końcu (jeśli są) nowe pliki uploadem – dwa osobne żądania; błąd `from-profile` pomija upload i pokazuje „Aplikacja złożona, ale nie udało się dołączyć załączników…”. Puste `documentIds` → 400 `No documents selected` (front nie wysyła pustych). Limity aplikacji jak przy uploadzie (te same komunikaty `Total size of attachments cannot exceed 25 MB`, `An application can have at most 10 attachments`); w dialogu limity są liczone łącznie (dokumenty z profilu + nowe pliki).
11. **Powiadomienia**: kandydat widzi tylko `applicationStatusChanged` (obejmuje też potwierdzenie złożenia aplikacji), pracodawca `newApplication` + `companyReview`, admin – nic (bez żądania GET). `PUT` wysyła zawsze wszystkie trzy pola (niewidoczne bez zmian) i oczekuje 200 z tym samym obiektem (puste body też jest obsłużone). Brakujące pole w GET = `true`.
12. **Zapisane oferty**: serduszka tylko dla zalogowanego kandydata (anonim, pracodawca, admin ich nie widzą; serwis nie woła API). `GET /ids` jest wołany raz na sesję kandydata (przy pierwszym użyciu serwisu), ponownie tylko po zmianie użytkownika lub po błędzie. `PUT`/`DELETE` → 204 bez body; 404 przy `PUT` (`Job offer not found`) cofa serduszko z komunikatem. `GET /api/saved-jobs` – `pageSize` 10, nagłówek `Pagination` jak w reszcie API, elementy `{ savedAt, job }` z pełnym `JobOfferDto` (`status`, `isExpired`). Usunięcie na stronie „Zapisane oferty” zostawia pozycję na liście (oznaczoną) do przeładowania – można ją przywrócić.
13. **Rate limit** (`resend-confirmation`, `forgot-password`) → 429 (treść dowolna); `forgot-password` zawsze 202 także dla nieistniejącego konta.

### Decyzje techniczne
- Baner ładowany leniwie przez `import()` + `ViewContainerRef.createComponent` w powłoce (`app.ts`) – `@defer` dokładał swój runtime do paczki startowej. Miejsce na baner (`.email-banner-slot`, 56 px / 72 px na wąskich ekranach, to samo tło) jest w układzie od pierwszego renderu (sesja odtwarzana synchronicznie), więc doładowanie nie powoduje CLS. `app.component.ts` (bootstrap) re-eksportuje `App` – jedna powłoka zamiast dwóch kopii.
- Przełączenie serduszka należy do `SavedJobsService` (żądanie nie jest przerywane zniszczeniem komponentu – PUT/DELETE są idempotentne, a stan musi być spójny między listą i szczegółami); komponent tylko przestaje nasłuchiwać. Odpowiedzi z poprzedniej sesji są ignorowane. Wynik ogłasza `LiveAnnouncer` (jeden region dla całej listy), błąd – snackbar.
- `AttachmentListComponent` dostał opcjonalne `actions` (pobieranie/usuwanie dokumentów profilu) i `itemNoun`; `AttachmentsPickerComponent` – `limitContext="profile"` (komunikaty limitów profilu). Wspólne `utils/http-transfer.ts` (pobieranie pliku z `Content-Disposition`, mapowanie postępu uploadu) używane przez `JobApplicationService` i `AccountService`.
- Żądania odczytu (profil, dokumenty, powiadomienia, zapisane oferty, potwierdzenie e-maila, reset hasła) są przerywane przy opuszczeniu strony. Zmiany hasła i usunięcia konta celowo nie – serwer mógł już je wykonać, a nowy token trzeba zapisać / sesję zakończyć.
- Nowe komponenty OnPush, wszystkie trasy lazy; `index.html` ma `lang="pl"`.

### Testy i build
- Pełne `ng test`: 59 plików, 1483 testy – zielone. `ng build`: initial 627,37 kB (przed etapem ~623 kB; +4,4 kB: rozszerzony AuthService, trasy, pozycje menu, slot banera), bez nowych ostrzeżeń budżetu (istniejące: initial > 500 kB, job-list.scss 5,13 kB).
- Uwaga środowiskowa: w kontenerze `jobboard-dev-node` testy z worktree (node_modules na montowaniu 9p z Windows) przy obciążeniu kończą się `[vitest-pool-runner]: Timeout waiting for worker to respond` (sztywny limit 60 s startu workera w Vitest). Uruchamiałem je z kopii w `/tmp/ua-fe` (node_modules skopiowane z worktree, `src` synchronizowane skryptem `/tmp/ua-sync.sh`).

### Do zrobienia po backendzie
- Smoke test na prawdziwym API (szczególnie kodowanie tokenów w linkach, `DELETE` z body, kształt ValidationProblem dla haseł).
- Opcjonalnie: „Dołącz z profilu” także w dialogu załączników w „Moich aplikacjach” (`ProfileDocumentsPickerComponent` jest gotowy do ponownego użycia).
