# Kontynuacja: proces rekrutacji i cykl życia ofert

Gałąź: `feature/recruitment-workflow` (niezmergowana, niewypchnięta).

## Stan

- `dotnet build`: 0 ostrzeżeń, 0 błędów; `dotnet test`: 247/247 zielonych.
- `npx ng build`: OK (tylko ostrzeżenia budżetów istniejące wcześniej: initial bundle 620 kB, `job-list.component.scss` 5.13 kB > 4 kB warning; brak błędów, limity nie zostały podniesione).
- `npx ng test --watch=false`: 375/375 zielonych.
- Migracja `20261104135648_AddRecruitmentWorkflow` zastosowana na `JobBoardDb` w dev MySQL (sprawdzony backfill na przykładowych aplikacjach, które potem usunięto; sprawdzone też down/up). Dump sprzed migracji: `/tmp/JobBoardDb_before_recruitment.sql` w kontenerze `jobboard-dev-mysql`.
- Smoke test API (Development, port 5099) przeszedł pełny przepływ: draft → publikacja → aplikacja → zmiana statusu z komentarzem → historia u kandydata (bez `changedBy`) → wycofanie → DELETE jako user = 403 → ponowna aplikacja (reaktywacja) → zamknięcie oferty → 400 przy aplikowaniu na zamkniętą.

## Zrobione

### Backend
- Modele: `Backend/Models/JobOfferStatus.cs`, `ApplicationStatus.cs`, `ApplicationStatusHistory.cs`; `JobOffer` (+`Status`, `ExpiresAt`, `IsExpired()`, `IsPubliclyVisible()`), `JobApplication` (+`Status`, `StatusChangedAt` jako token współbieżności, `StatusHistory`, `Submit()`, `ChangeStatus()`).
- `Backend/Data/ApplicationDbContext.cs`: konfiguracja nowych encji, konwencja UTC dla wszystkich `DateTime` (API zwraca daty z `Z`).
- Migracja: domyślnie `Status = 1` (Active) dla ofert, `StatusChangedAt = AppliedAt`, backfill historii (`INSERT … SELECT`).
- `JobsController`: publiczna lista tylko widocznych ofert, `GET /api/jobs/manage` (filtr `state`), `PATCH /api/jobs/{id}/status`, draft → 404 dla nie-admina, walidacja `ExpiresAt` w `CreateJobOfferDto`.
- `JobApplicationsController`: `PUT {id}/status`, `POST {id}/withdraw`, reaktywacja po wycofaniu, rozszerzony `check/{jobId}`, historia w `GET {id}` (`changedBy` tylko dla admina), filtr `status` w `/all`, DELETE tylko dla admina, 400 przy aplikowaniu na niewidoczną ofertę.
- Seeder: 20 aktywnych ofert z `ExpiresAt` za 14–60 dni + przykład szkicu, zamkniętej i wygasłej.
- Testy: zaktualizowane `JobApplicationsTests` (DELETE), `MigrationTests` (dane sprzed migracji przez SQL + nowy test migracji z backfillem).

### Frontend
- Modele/enumy, `utils/status-labels.ts` (polskie etykiety, kolory, `getJobState`, `canWithdraw`, `formatExpiresSoon`), tłumaczenia komunikatów w `utils/http-errors.ts`.
- Wspólne komponenty: `components/shared/status-chip.component.ts`, `status-timeline.component.ts`.
- Panel admina – oferty: `/api/jobs/manage`, filtr stanu, kolumna stanu, akcje Opublikuj/Zamknij/Wznów; dialog oferty: status + datepicker „Aktualna do” (wysyłany koniec wybranego dnia lokalnego jako ISO UTC; niezmieniony dzień przy edycji zachowuje oryginalną datę).
- Panel admina – aplikacje: kolumna i filtr statusu, dialog „Zmień status” (z podpowiedzią o widoczności komentarza), dialog „Historia”.
- „Moje aplikacje”: chip statusu, rozwijana historia, „Wycofaj” z potwierdzeniem (usuwanie przez DELETE usunięte).
- Szczegóły oferty: baner dla zamkniętej/wygasłej/szkicu, blokada aplikowania, status aplikacji, „Aplikuj ponownie”, „Aktualna do”.
- Lista ofert: „Wygasa dziś / jutro / za X dni” (≤ 7 dni).
- README: sekcje „Cykl życia ofert” i „Proces rekrutacji”.

## Czego brakuje

- Dedykowane testy backendu nowych funkcji (planowane dla osobnego agenta): `Backend.Tests/Integration/` – `/api/jobs/manage` (każdy `state`, 400 dla złego `state`), `PATCH /api/jobs/{id}/status`, widoczność draft/closed/expired w `GET /api/jobs` i `GET /api/jobs/{id}`, walidacja `ExpiresAt`, `PUT /api/jobapplications/{id}/status` (wszystkie przypadki 400/404/409), `withdraw`, reaktywacja, `check`, historia i `changedBy`, filtr `status` w `/all`, seed (3 niewidoczne oferty); `Backend.Tests/Unit/MappingExtensionsTests.cs` – nowe pola i historia.
- Testy frontendu dla nowych dialogów: `components/admin-panel/application-status-dialog/`, `application-history-dialog/`, `components/shared/` (brak plików `.spec.ts`); dodatkowe przypadki w `admin-applications-tab.component.spec.ts` (filtr statusu, otwieranie dialogów).
- Brak testów E2E/ręcznego przejścia UI w przeglądarce.

## Znane problemy / ryzyka

- Aplikowanie na szkic zwraca 400 (zgodnie ze specyfikacją), a `GET /api/jobs/{id}` dla szkicu zwraca 404 – endpoint aplikowania pozwala więc stwierdzić istnienie szkicu po id.
- `PUT /api/jobs/{id}` bez pola `status` ustawia `Active` (DTO jest pełną reprezentacją oferty) – stary klient edytujący szkic opublikowałby go.
- Admin może ustawić dowolne przejście statusu (np. Odrzucona → W analizie) – brak maszyny stanów.
- Równoległe zmiany statusu tej samej aplikacji: druga dostaje 409 (`Application status was changed by another request`); notatki (ten sam status) nie są objęte kontrolą współbieżności.
- Kolumna `JobOffers.Status` ma w bazie `DEFAULT 1`, którego nie ma w modelu EF (celowo – inaczej EF pomijałby `Draft` przy INSERT).
- Konwencja UTC zmienia serializację wszystkich dat (teraz z sufiksem `Z`) – frontend wyświetla je w czasie lokalnym.
- Seedowe oferty wygasają po 14–60 dniach na długo działającej bazie demo (reset danych je odnawia). Istniejące bazy po migracji mają oferty bezterminowe.
- Pierwszy test w specach zakładek admina trwa ok. 3 s (kompilacja komponentu); przy zimnym starcie i obciążeniu raz przekroczył 5 s timeout vitest – kolejne przebiegi stabilne.

## Kolejne kroki

1. Przejść UI ręcznie (panel admina, „Moje aplikacje”, szczegóły oferty) i poprawić ewentualne usterki wizualne.
2. Dopisać brakujące testy (lista wyżej) – osobny agent (Sonnet).
3. Review zmian (backend + frontend), decyzja w sprawie ryzyk z listy.
4. Merge `feature/recruitment-workflow` do `main`.
