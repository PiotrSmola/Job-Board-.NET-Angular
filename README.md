# Job-Board-.NET-Angular

## Uruchomienie w Dockerze

Wymagany Docker Desktop (Docker Compose v2).

```powershell
.\docker-up.ps1
```

Skrypt buduje obrazy i uruchamia trzy kontenery: MySQL, backend (ASP.NET Core) i frontend (Angular serwowany przez nginx, który przekazuje `/api` do backendu). Domyślne porty to 4200 (frontend), 5285 (backend / Swagger) i 3306 (MySQL) - jeśli któryś jest zajęty, wybierany jest kolejny wolny. Wybrane porty zapisywane są do `.env` i wypisywane na koniec.

Preferowane porty można zmienić parametrami:

```powershell
.\docker-up.ps1 -FrontendPort 8080 -BackendPort 8081 -DbPort 3310
```

> **Uwaga:** stack z `docker-compose.yml` jest przeznaczony wyłącznie do lokalnego uruchomienia (demo). Konta demo (tabela niżej) i root MySQL mają znane hasła, a admin może z panelu wyczyścić bazę, dlatego wszystkie porty są publikowane tylko na `127.0.0.1` - aplikacja nie jest dostępna z innych komputerów w sieci. Nie wystawiaj go publicznie bez zmiany haseł i wyłączenia `ALLOW_SEED_REFRESH`.

Zatrzymanie: `docker compose down` (dane bazy zostają w wolumenie `db-data`; `docker compose down -v` usuwa je).

Bez skryptu można też użyć bezpośrednio `docker compose up -d --build` - porty brane są wtedy z `.env` (wzór w `.env.example`) lub domyślne. W `.env` musi wtedy być ustawiony `JWT_SECRET`.

Zmienne w `.env`:

- `JWT_SECRET` - sekret do podpisywania tokenów JWT (min. 32 znaki). Wymagany: bez niego `docker compose` nie wystartuje. `docker-up.ps1` przy pierwszym uruchomieniu generuje losowy 64-znakowy sekret i zapisuje go w `.env`, przy kolejnych zachowuje istniejący (zmiana sekretu unieważnia zalogowane sesje).
- `ALLOW_SEED_REFRESH` - czy admin może z panelu zresetować dane demo (`POST /api/admin/refresh-seed` usuwa **wszystkich** użytkowników, firmy, oferty i aplikacje i odtwarza dane startowe). W lokalnym stacku Dockera domyślnie `true`; ustaw `false`, żeby wyłączyć. Poza Dockerem reset działa tylko w środowisku Development albo po ustawieniu `Admin:AllowSeedRefresh=true`.

## Konta testowe

Tworzone automatycznie przy pierwszym starcie (i po resecie danych demo). Hasła są publicznie znane - konta służą tylko do lokalnego demo:

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

Rola trafia do tokenu JWT (claim `role`), ale firma pracodawcy jest odczytywana z bazy przy każdym żądaniu, więc zmiana przypisania działa bez ponownego logowania. Sprawdzanie uprawnień do zasobów jest w jednym miejscu (`ICurrentUserAccess`): admin zarządza wszystkim, pracodawca tylko ofertami i aplikacjami swojej firmy - oferta albo aplikacja innej firmy zwraca `404` (bez ujawniania, że istnieje).

### Weryfikacja firm

1. Pracodawca zakłada konto z firmą (`POST /api/auth/register-employer`, przełącznik „Rekrutuję” na stronie rejestracji). Konto i firma powstają w jednej transakcji; nazwa firmy jest unikalna bez rozróżniania wielkości liter (odstępy na brzegach i wielokrotne spacje są usuwane), zajęta nazwa zwraca `400`.
2. Firma ma status **Oczekuje** (`Pending`). Pracodawca może już tworzyć i edytować szkice ofert, ale publikacja (status `Active`) zwraca `403` (`Company must be approved before publishing offers`).
3. Admin w zakładce „Firmy” zatwierdza firmę (`POST /api/admin/companies/{id}/approve`) albo odrzuca ją z powodem (`POST /api/admin/companies/{id}/reject`). Powód widzi pracodawca w profilu firmy.
4. Odrzucona firma po edycji profilu przez pracodawcę (`PUT /api/employer/company`) wraca do statusu **Oczekuje** - to ponowne zgłoszenie do weryfikacji.
5. Zatwierdzona firma wraca do statusu **Oczekuje**, gdy pracodawca zmieni jej nazwę (inaczej niż tylko wielkością liter lub odstępami) albo stronę www - chroni to przed podszyciem się pod inną markę po weryfikacji. Do ponownego zatwierdzenia profil i oferty firmy nie są widoczne publicznie (panel pracodawcy ostrzega o tym przed zapisem). Zmiana samego opisu lub lokalizacji nie zmienia statusu, a edycja przez admina (`PUT /api/admin/companies/{id}`) nigdy go nie zmienia.

Publicznie widoczne są tylko firmy zatwierdzone (`GET /api/companies`, profil `GET /api/companies/{id}`) i tylko ich oferty: odrzucenie firmy ukrywa jej oferty z listy, szczegółów i aplikowania (`404`), ale ich nie usuwa. Admin może też dodać firmę od razu zatwierdzoną, edytować ją i usunąć (tylko firmę bez ofert i bez kont pracodawców). Przy tworzeniu oferty admin wskazuje firmę (`companyId`), pracodawca zawsze publikuje w swojej.

Migracja `AddCompanies` zamienia tekstową nazwę firmy w ofertach na firmy: dla każdej nazwy (po usunięciu zbędnych odstępów, bez rozróżniania wielkości liter) powstaje zatwierdzona firma z datą utworzenia najstarszej oferty i pisownią nazwy z tej oferty. Cofnięcie migracji przywraca nazwę firmy w ofertach (w ujednoliconej pisowni); przypisania pracodawców i firmy bez ofert są wtedy tracone.

## Cykl życia ofert

Oferta ma status zapisany w bazie oraz opcjonalną datę ważności (`expiresAt`, UTC):

| Stan | Znaczenie | Lista publiczna | Aplikowanie |
| --- | --- | --- | --- |
| Szkic (`Draft`) | oferta w przygotowaniu | nie (szczegóły tylko dla admina i pracodawcy firmy) | nie |
| Aktywna (`Active`) | opublikowana, bez daty lub z datą w przyszłości | tak | tak |
| Wygasła | `Active` z datą ważności w przeszłości - stan wyliczany, nie zapisywany | nie (szczegóły z banerem) | nie |
| Zamknięta (`Closed`) | rekrutacja zakończona | nie (szczegóły z banerem) | nie |

Admin i pracodawca (tylko oferty swojej firmy) zarządzają ofertami w panelu (`GET /api/jobs/manage` z filtrem `state=draft|active|expired|closed`): szybkie akcje Opublikuj / Zamknij / Wznów (`PATCH /api/jobs/{id}/status`) oraz status i datę „Aktualna do” w formularzu oferty (oferta jest ważna do końca wybranego dnia). Aktywnej oferty nie można opublikować, wznowić ani zapisać z nową datą ważności w przeszłości - trzeba ją przedłużyć albo zamknąć; wygasłą ofertę można natomiast edytować bez zmiany daty (pozostaje wygasła). `PUT /api/jobs/{id}` bez pola `status` zachowuje obecny status oferty (`POST` bez statusu tworzy ofertę aktywną). Istniejące oferty po migracji pozostają aktywne i bezterminowe.

## Proces rekrutacji

Każda aplikacja ma status, a każda jego zmiana (także złożenie aplikacji) trafia do historii z datą, autorem i opcjonalnym komentarzem:

`Złożona` → `W analizie` → `Rozmowa` → `Oferta` → `Zatrudniony`, w każdej chwili `Odrzucona`; kandydat może też `Wycofać` aplikację.

- **Rekruter (admin albo pracodawca firmy oferty)** zmienia status w panelu (`PUT /api/jobapplications/{id}/status`) i może dodać komentarz - **komentarz jest widoczny dla kandydata**. Ten sam status z komentarzem zapisuje notatkę bez zmiany statusu. Statusu „Wycofana” nie ustawia rekruter, a aplikacji wycofanej nie można już zmieniać. Trwale usunąć aplikację może tylko admin.
- **Kandydat** widzi status i historię w „Moich aplikacjach” (bez danych autorów zmian) i może wycofać aplikację na etapie Złożona / W analizie / Rozmowa / Oferta (`POST /api/jobapplications/{id}/withdraw`). Po wycofaniu może aplikować ponownie, dopóki oferta jest aktywna - reaktywowana zostaje ta sama aplikacja (z nową notatką i datą).
- Aplikować można tylko na oferty aktywne i niewygasłe (próba aplikowania na szkic zwraca dla kandydata `404`, tak jak dla nieistniejącej oferty).
- Równoległe zmiany tej samej aplikacji (także notatki i wycofanie) są chronione tokenem współbieżności: żądanie oparte na nieaktualnym stanie dostaje `409` (`Application status was changed by another request`), a równoległa ponowna aplikacja - `400` (`You have already applied to this job`).

## Uruchomienie lokalne (bez Dockera)

Backend: `dotnet run` w katalogu `Backend` (wymaga MySQL na `localhost:3306`). W środowisku Development, gdy nie podano `JwtSettings:Secret`, używany jest wbudowany klucz deweloperski; w innych środowiskach sekret jest wymagany (np. zmienna `JwtSettings__Secret`). Frontend: `npm start` w katalogu `frontend` - `ng serve` przekazuje `/api` do `http://localhost:5285` (`frontend/proxy.conf.json`).

## Testy

Testy backendu (xUnit) są w katalogu `Backend.Tests` (dodany do `Backend/Backend.sln`): testy jednostkowe (`Unit/`) oraz integracyjne (`Integration/`) uruchamiające aplikację przez `WebApplicationFactory` na prawdziwym MySQL.

```bash
cd Backend.Tests
dotnet test
```

Testy integracyjne wymagają działającego serwera MySQL. Każda fabryka tworzy własną bazę o nazwie `JobBoardTest_<guid>` (migracje i seed przy starcie aplikacji) i usuwa ją po zakończeniu - baza `JobBoardDb` nie jest dotykana. Serwer i dane logowania są brane ze zmiennej środowiskowej `JOBBOARD_TEST_CONNECTION` (connection string MySQL; nazwa bazy jest ignorowana i zastępowana), a gdy jej brak - ze zmiennej `ConnectionStrings__DefaultConnection` (też tylko serwer i dane logowania). Bez żadnej z nich używane jest `Server=localhost;Port=3306;User=root;Password=devpass`.

Przykład: `JOBBOARD_TEST_CONNECTION="Server=localhost;Port=3306;User=root;Password=secret;AllowPublicKeyRetrieval=true;" dotnet test`.
