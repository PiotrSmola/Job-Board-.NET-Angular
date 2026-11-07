import { Page } from '@playwright/test';
import { expect, makePdf, scanA11y, test } from './fixtures/test';
import { authFile, readState, writeState } from './fixtures/state';

test.use({ storageState: authFile('candidate') });

const snackBar = (page: Page) => page.locator('.mat-mdc-snack-bar-container');

test.describe('kandydat – zapisane oferty', () => {
  test('serduszko na liście i stronie oferty, strona Zapisane, usunięcie z zapisanych', async ({ page }) => {
    await page.goto('/jobs');
    const card = page.locator('article.job-card').first();
    await expect(card).toBeVisible();
    const title = (await card.locator('h3').innerText()).trim();

    const heart = card.getByRole('button', { name: `Zapisz ofertę: ${title}` });
    await heart.click();
    // klik w serce nie otwiera szczegółów oferty
    await expect(page).toHaveURL(/\/jobs$/);
    await expect(card.getByRole('button', { name: `Usuń z zapisanych: ${title}` })).toHaveAttribute('aria-pressed', 'true');

    // druga oferta zapisana ze strony szczegółów
    await page.locator('article.job-card h3 a').nth(1).click();
    await expect(page.locator('h1#offer-title')).toBeVisible();
    const secondTitle = (await page.locator('h1#offer-title').innerText()).trim();
    await page.getByRole('button', { name: `Zapisz ofertę: ${secondTitle}` }).click();
    await expect(page.getByRole('button', { name: `Usuń z zapisanych: ${secondTitle}` })).toBeVisible();

    await page.getByRole('link', { name: 'Zapisane' }).click();
    await expect(page).toHaveURL(/\/saved-jobs/);
    await expect(page).toHaveTitle('Zapisane oferty – Job Board');
    await expect(page.getByRole('heading', { level: 1, name: 'Zapisane oferty' })).toBeVisible();
    await expect(page.locator('article.item')).toHaveCount(2);
    await expect(page.locator('article.item', { hasText: title })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Zapisane' })).toHaveAttribute('aria-current', 'page');

    // stan przetrwał przeładowanie (zapis po stronie serwera)
    await page.reload();
    await expect(page.locator('article.item')).toHaveCount(2);

    // usunięcie: pozycja zostaje z komunikatem „Usunięto z zapisanych”, po przeładowaniu znika
    await page.locator('article.item', { hasText: secondTitle }).getByRole('button', { name: `Usuń z zapisanych: ${secondTitle}` }).click();
    await expect(page.locator('article.item', { hasText: secondTitle }).getByText('Usunięto z zapisanych')).toBeVisible();
    await page.reload();
    await expect(page.locator('article.item')).toHaveCount(1);
    await scanA11y(page, 'zapisane oferty');

    // sprzątanie – ostatnia pozycja i pusty stan
    await page.getByRole('button', { name: `Usuń z zapisanych: ${title}` }).click();
    await page.reload();
    await expect(page.getByText('Nie masz jeszcze zapisanych ofert')).toBeVisible();
  });
});

test.describe('kandydat – Moje konto', () => {
  test('menu sekcji, linki ?tab= i fokus na nagłówku sekcji', async ({ page }) => {
    await page.goto('/account');
    await expect(page).toHaveTitle('Moje konto – Job Board');
    const nav = page.getByRole('navigation', { name: 'Sekcje konta' });
    for (const name of ['Profil', 'Dokumenty', 'Hasło i bezpieczeństwo', 'Powiadomienia']) {
      await expect(nav.getByRole('link', { name })).toBeVisible();
    }
    await expect(nav.getByRole('link', { name: 'Profil' })).toHaveAttribute('aria-current', 'true');

    await nav.getByRole('link', { name: 'Dokumenty' }).click();
    await expect(page).toHaveURL(/\/account\?tab=documents/);
    await expect(page.getByRole('heading', { level: 2, name: 'Dokumenty' })).toBeFocused();
    await expect(nav.getByRole('link', { name: 'Dokumenty' })).toHaveAttribute('aria-current', 'true');
    // zmiana sekcji podmienia wpis historii (replaceUrl)
    const length = await page.evaluate(() => history.length);
    await nav.getByRole('link', { name: 'Powiadomienia' }).click();
    await expect(page).toHaveURL(/tab=notifications/);
    expect(await page.evaluate(() => history.length)).toBe(length);

    // bezpośrednie wejście z linku (np. z dialogu aplikowania lub maila)
    await page.goto('/account?tab=security');
    await expect(page.getByRole('heading', { level: 2, name: 'Hasło i bezpieczeństwo' })).toBeFocused();
    await expect(page.getByRole('heading', { level: 3, name: 'Zmiana hasła' })).toBeVisible();
    await page.goto('/account?tab=documents');
    await expect(page.getByRole('heading', { level: 2, name: 'Dokumenty' })).toBeFocused();
    // nieznana wartość wraca do profilu
    await page.goto('/account?tab=nieznana');
    await expect(nav.getByRole('link', { name: 'Profil' })).toHaveAttribute('aria-current', 'true');

    // klawiatura: Tab dociera do linków menu sekcji, Enter przewija
    await nav.getByRole('link', { name: 'Profil' }).focus();
    await page.keyboard.press('Tab');
    await expect(nav.getByRole('link', { name: 'Dokumenty' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { level: 2, name: 'Dokumenty' })).toBeFocused();
  });

  test('profil: walidacja, zapis i trwałość zmian', async ({ page }) => {
    await page.goto('/account');
    const profile = page.locator('#profil');
    await expect(profile.getByText('(potwierdzony)')).toBeVisible();
    // baner o niepotwierdzonym e-mailu nie wyświetla się po potwierdzeniu
    await expect(page.locator('.email-banner-slot')).toHaveCount(0);

    await profile.getByLabel('Imię').fill('');
    await profile.getByRole('button', { name: 'Zapisz zmiany' }).click();
    await expect(profile.getByText(/Imię jest wymagane/)).toBeVisible();
    await profile.getByLabel('Imię').fill('Ewelina');
    await profile.getByLabel(/Nagłówek/).fill('Tester interfejsu');
    await profile.getByLabel(/Lokalizacja/).fill('Rzeszów');
    await profile.getByLabel(/LinkedIn/).fill('nie-link');
    await profile.getByRole('button', { name: 'Zapisz zmiany' }).click();
    await expect(profile.getByText(/Podaj adres profilu LinkedIn/)).toBeVisible();
    await profile.getByLabel(/LinkedIn/).fill('https://www.linkedin.com/in/e2e-tester');
    await profile.getByLabel(/O mnie/).fill('Opis kandydata testowego');
    await expect(profile.getByText('24 z ')).toBeVisible();
    await profile.getByRole('button', { name: 'Zapisz zmiany' }).click();
    await expect(snackBar(page)).toContainText('Zapisano zmiany w profilu');

    await page.reload();
    await expect(profile.getByLabel('Imię')).toHaveValue('Ewelina');
    await expect(profile.getByLabel(/Nagłówek/)).toHaveValue('Tester interfejsu');
    // inicjały w menu konta pochodzą z nowych danych
    await expect(page.getByRole('button', { name: /Menu konta: Ewelina/ })).toBeVisible();
  });

  test('dokumenty: dodanie PDF, pobranie, błędy walidacji pliku', async ({ page }) => {
    await page.goto('/account?tab=documents');
    const section = page.locator('#dokumenty');
    await expect(section.getByText('Nie masz jeszcze dokumentów w profilu')).toBeVisible();
    const upload = section.getByRole('button', { name: 'Prześlij dokument' });
    await expect(upload).toHaveAttribute('aria-disabled', 'true');

    // nieobsługiwany typ pliku jest odrzucany po stronie klienta
    await section.locator('input[type=file]').setInputFiles({ name: 'skrypt.exe', mimeType: 'application/octet-stream', buffer: Buffer.from('MZ') });
    await expect(section.getByRole('alert')).toBeVisible();

    await section.locator('input[type=file]').setInputFiles({ name: 'cv-e2e.pdf', mimeType: 'application/pdf', buffer: makePdf('cv') });
    await expect(section.locator('.file-name', { hasText: 'cv-e2e.pdf' })).toBeVisible();
    await section.getByRole('button', { name: 'Prześlij dokument' }).click();
    await expect(snackBar(page)).toContainText('Dokument został dodany');
    const download = section.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ });
    await expect(download).toBeVisible();

    const [file] = await Promise.all([page.waitForEvent('download'), download.click()]);
    expect(file.suggestedFilename()).toBe('cv-e2e.pdf');

    await page.reload();
    await expect(page.locator('#dokumenty').getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ })).toBeVisible();
  });

  test('powiadomienia: przełączenie preferencji i zapis', async ({ page }) => {
    await page.goto('/account?tab=notifications');
    const section = page.locator('#powiadomienia');
    const boxes = section.getByRole('checkbox');
    await expect(boxes.first()).toBeVisible();
    const before = await boxes.first().isChecked();
    await boxes.first().setChecked(!before);
    await section.getByRole('button', { name: 'Zapisz ustawienia' }).click();
    await expect(snackBar(page)).toContainText('Zapisano ustawienia powiadomień');
    await page.reload();
    await expect(page.locator('#powiadomienia').getByRole('checkbox').first()).toBeChecked({ checked: !before });
    // przywrócenie
    await page.locator('#powiadomienia').getByRole('checkbox').first().setChecked(before);
    await page.locator('#powiadomienia').getByRole('button', { name: 'Zapisz ustawienia' }).click();
    await expect(snackBar(page)).toContainText('Zapisano ustawienia powiadomień');
  });

  test('bezpieczeństwo: walidacja zmiany hasła i usuwania konta (bez wykonania)', async ({ page, monitor }) => {
    monitor.allow(/\/api\/auth\/change-password 400/, /Failed to load resource.*400/);
    await page.goto('/account?tab=security');
    const section = page.locator('#haslo');
    await section.locator('input[formcontrolname="currentPassword"]').fill('ZleHaslo123!');
    await section.locator('input[formcontrolname="newPassword"]').fill('slabe');
    await section.locator('input[formcontrolname="confirmPassword"]').fill('inne');
    await section.getByRole('button', { name: 'Zmień hasło' }).click();
    await expect(section.getByText(/Hasło musi mieć/)).toBeVisible();
    await expect(section.getByText('Hasła nie są takie same')).toBeVisible();

    await section.getByRole('button', { name: /Usuń konto/ }).click();
    await expect(section.locator('input[formcontrolname="password"]')).toBeVisible();
    await section.getByRole('button', { name: 'Anuluj' }).click();
    await expect(section.locator('input[formcontrolname="password"]')).toBeHidden();
    await scanA11y(page, 'moje konto – hasło i bezpieczeństwo');
  });

  test('skan dostępności całej strony konta', async ({ page }) => {
    await page.goto('/account');
    await expect(page.locator('#dokumenty').getByRole('button', { name: /Pobierz/ })).toBeVisible();
    await scanA11y(page, 'moje konto');
    await page.setViewportSize({ width: 375, height: 800 });
    await page.reload();
    await expect(page.locator('#profil')).toBeVisible();
    await scanA11y(page, 'moje konto 375px');
  });
});

test.describe('kandydat – aplikowanie i Moje aplikacje', () => {
  test('aplikuje na stronie oferty: dokument z profilu + nowy PDF + wiadomość', async ({ page }) => {
    const offer = readState().offer;
    expect(offer?.id, 'oferta pracodawcy z 20-employer-offers').toBeTruthy();
    await page.goto(`/jobs/${offer!.id}`);
    await expect(page.locator('h1#offer-title')).toHaveText(offer!.title);

    // przycisk w nagłówku przenosi fokus do sekcji aplikowania
    await page.getByRole('button', { name: 'Aplikuj' }).click();
    const section = page.locator('#apply');
    await expect(section.getByRole('heading', { name: 'Twoja aplikacja' })).toBeFocused();
    await expect(section.getByRole('group', { name: 'Dołącz z profilu' })).toBeVisible();

    await section.getByRole('checkbox', { name: /cv-e2e\.pdf/ }).check();
    await section.locator('input[type=file]').setInputFiles({ name: 'list-motywacyjny.pdf', mimeType: 'application/pdf', buffer: makePdf('list') });
    await expect(section.locator('.file-name', { hasText: 'list-motywacyjny.pdf' })).toBeVisible();
    await section.getByLabel(/Wiadomość do rekrutera/).fill('Wiadomość kandydata z testu E2E');
    await expect(section.getByText('31 / ')).toBeVisible();

    await section.getByRole('button', { name: 'Wyślij aplikację' }).click();
    await expect(section.getByText('Już aplikowałeś na tę ofertę.')).toBeVisible({ timeout: 30_000 });
    await expect(section.getByText('Złożona')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Twoja aplikacja' })).toBeVisible();
    writeState({ appliedJob: { id: offer!.id!, title: offer!.title } });

    // po przeładowaniu strona nadal wie o aplikacji
    await page.reload();
    await expect(page.locator('#apply').getByText('Już aplikowałeś na tę ofertę.')).toBeVisible();
    await scanA11y(page, 'oferta po aplikowaniu');
  });

  test('Moje aplikacje: zakładki, rozwijanie, historia, załączniki i pobranie', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/jobs');
    await page.getByRole('link', { name: 'Moje aplikacje' }).click();
    await expect(page).toHaveURL(/\/my-applications/);
    await expect(page).toHaveTitle('Moje aplikacje – Job Board');

    const tabs = page.getByRole('tablist', { name: 'Filtr aplikacji' });
    await expect(tabs.getByRole('tab', { name: /W toku \(1\)/ })).toHaveAttribute('aria-selected', 'true');
    await expect(tabs.getByRole('tab', { name: /Zakończone \(0\)/ })).toBeVisible();

    const article = page.locator('article.application', { hasText: job.title });
    await expect(article).toBeVisible();
    await expect(article.getByText('Złożona')).toBeVisible();
    const toggle = article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` });
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await expect(article.getByRole('heading', { level: 3, name: 'Historia' })).toBeVisible();
    await expect(article.getByText('Wiadomość kandydata z testu E2E')).toBeVisible();
    await expect(article.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ })).toBeVisible();
    await expect(article.getByRole('button', { name: /Pobierz .*: list-motywacyjny\.pdf/ })).toBeVisible();

    const [file] = await Promise.all([
      page.waitForEvent('download'),
      article.getByRole('button', { name: /Pobierz .*: list-motywacyjny\.pdf/ }).click()
    ]);
    expect(file.suggestedFilename()).toBe('list-motywacyjny.pdf');

    // zakładka „Zakończone” jest pusta
    await tabs.getByRole('tab', { name: /Zakończone/ }).click();
    await expect(page.getByText('Nie masz zakończonych aplikacji.')).toBeVisible();
    await tabs.getByRole('tab', { name: /W toku/ }).click();
    await expect(article).toBeVisible();
    await scanA11y(page, 'moje aplikacje');
  });

  test('dodanie załącznika do trwającej aplikacji przez okno dialogowe', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/my-applications');
    const article = page.locator('article.application', { hasText: job.title });
    await article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` }).click();
    const add = article.getByRole('button', { name: /Dodaj załączniki/ });
    await add.click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await scanA11y(page, 'dialog dodawania załączników');
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(add).toBeFocused();
  });
});
