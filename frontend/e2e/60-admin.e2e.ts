import { Page } from '@playwright/test';
import { expect, makePng, scanA11y, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

test.use({ storageState: authFile('admin') });

const snackBar = (page: Page) => page.locator('.mat-mdc-snack-bar-container');

async function openAdmin(page: Page, tab?: string): Promise<void> {
  await page.goto('/admin');
  await expect(page.getByRole('heading', { level: 1, name: 'Panel admina' })).toBeVisible();
  if (tab) {
    await page.getByRole('tab', { name: tab }).click();
    await expect(page.getByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true');
  }
}

test.describe('admin – statystyki', () => {
  test('zakresy dat, kafelki, wykres i przełącznik tabeli danych', async ({ page }) => {
    await openAdmin(page);
    await expect(page).toHaveTitle('Panel admina – Job Board');
    const stats = page.getByRole('region', { name: 'Statystyki' });
    await expect(stats.locator('dl.tiles')).toBeVisible();
    expect(await stats.locator('dl.tiles .tile').count()).toBeGreaterThan(2);

    const presets = stats.getByRole('group', { name: 'Gotowe zakresy dat' });
    for (const label of ['7 dni', '30 dni', '90 dni', '12 miesięcy']) {
      const button = presets.getByRole('button', { name: label });
      const response = page.waitForResponse(r => r.url().includes('/api/') && r.url().includes('stats') && r.request().method() === 'GET');
      await button.click();
      await response;
      await expect(button).toHaveAttribute('aria-pressed', 'true');
    }

    // przełącznik „Pokaż jako tabelę” – prawdziwa tabela z danymi zamiast wykresu
    const toggle = stats.getByRole('switch', { name: 'Pokaż jako tabelę' }).first();
    if (await toggle.count()) {
      await toggle.click();
      await expect(stats.getByRole('region', { name: /tabela danych/ })).toBeVisible();
      await expect(stats.locator('table caption').first()).toBeAttached();
      await toggle.click();
      await expect(stats.getByRole('region', { name: /tabela danych/ })).toBeHidden();
      // legenda wykresu to przyciski aria-pressed
      const legendButton = stats.getByRole('list', { name: /Legenda/ }).getByRole('button').first();
      await legendButton.click();
      await expect(legendButton).toHaveAttribute('aria-pressed', 'false');
      await legendButton.click();
      await expect(legendButton).toHaveAttribute('aria-pressed', 'true');
    }

    // własny zakres: za długi zakres daje komunikat
    await stats.getByRole('button', { name: 'Odśwież statystyki' }).click();
    await expect(stats.getByText('Wczytywanie')).toHaveCount(0);

    const [csv] = await Promise.all([page.waitForEvent('download'), stats.getByRole('button', { name: 'Eksportuj CSV' }).click()]);
    expect(csv.suggestedFilename()).toMatch(/\.csv$/);
    await scanA11y(page, 'panel admina – statystyki');
  });

  test('sekcja „Firmy do weryfikacji” i okno odrzucenia (bez zmiany stanu)', async ({ page }) => {
    await openAdmin(page);
    const pending = page.getByRole('region', { name: /Firmy do weryfikacji/ });
    await expect(pending).toBeVisible();
    const rejectButton = pending.getByRole('button', { name: /^Odrzuć firmę/ }).first();
    if (await rejectButton.count()) {
      await rejectButton.click();
      const dialog = page.getByRole('dialog', { name: /Odrzuć firmę/ });
      await expect(dialog.getByLabel('Powód odrzucenia')).toBeFocused();
      // puste uzasadnienie blokuje wysłanie
      await expect(dialog.getByRole('button', { name: 'Odrzuć' })).toBeDisabled();
      await page.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
      await expect(rejectButton).toBeFocused();
    } else {
      await expect(pending.getByText('Brak firm oczekujących na weryfikację.')).toBeVisible();
    }
  });
});

test.describe('admin – firmy (utworzona w teście, nie firmy demo)', () => {
  test('dodaje firmę, odrzuca z powodem, zatwierdza i usuwa', async ({ page }) => {
    const runId = readState().runId!;
    const name = `E2E Firma ${runId}`;
    await openAdmin(page, 'Firmy');

    await page.getByRole('button', { name: 'Dodaj firmę' }).first().click();
    const dialog = page.getByRole('dialog', { name: 'Dodaj firmę' });
    await expect(dialog.getByLabel('Nazwa firmy')).toBeFocused();
    await dialog.getByLabel('Nazwa firmy').fill(name);
    await dialog.getByLabel('Miasto').fill('Rzeszów');
    await dialog.getByLabel('Strona internetowa').fill('nie-adres');
    await dialog.getByLabel('Opis').fill('Firma utworzona w teście E2E');
    // niepoprawny adres www blokuje zapis
    await expect(dialog.locator('button.submit-button')).toBeDisabled();
    await dialog.getByLabel('Strona internetowa').fill('https://e2e.example.com');
    await dialog.locator('button.submit-button').click();
    await expect(dialog).toBeHidden();

    await page.getByRole('textbox', { name: 'Szukaj' }).fill(name);
    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible();

    // odrzucenie z powodem
    await row.getByRole('button', { name: `Odrzuć firmę: ${name}` }).click();
    const reject = page.getByRole('dialog', { name: /Odrzuć firmę/ });
    await reject.getByLabel('Powód odrzucenia').fill('Powód z testu E2E');
    await reject.getByRole('button', { name: 'Odrzuć' }).click();
    await expect(reject).toBeHidden();
    await expect(row.getByText('Odrzucona')).toBeVisible();
    await expect(row).toContainText('Powód z testu E2E');

    // filtr statusu
    await page.getByRole('combobox', { name: 'Status' }).click();
    await page.getByRole('option', { name: 'Odrzucona' }).click();
    await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible();
    await page.getByRole('button', { name: 'Wyczyść filtry' }).click();
    await page.getByRole('textbox', { name: 'Szukaj' }).fill(name);

    // zatwierdzenie
    await row.getByRole('button', { name: `Zatwierdź firmę: ${name}` }).click();
    await expect(row.getByText('Zatwierdzona')).toBeVisible();
    await expect(snackBar(page)).toContainText('została zatwierdzona');
    await scanA11y(page, 'panel admina – firmy');

    // usunięcie (potwierdzenie)
    await row.getByRole('button', { name: `Usuń firmę: ${name}` }).click();
    const confirm = page.getByRole('dialog', { name: 'Usunąć firmę?' });
    await expect(confirm.getByRole('button', { name: 'Anuluj' })).toBeFocused();
    await confirm.getByRole('button', { name: 'Usuń' }).click();
    await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(0);
  });
});

test.describe('admin – kategorie', () => {
  test('dodaje, edytuje i usuwa własną kategorię; kategoria jest widoczna w filtrach listy', async ({ page, browser }) => {
    const runId = readState().runId!;
    const name = `E2E Kategoria ${runId}`;
    const slug = `e2e-${runId}`;
    await openAdmin(page, 'Kategorie');

    await page.getByRole('button', { name: 'Dodaj kategorię' }).click();
    const dialog = page.getByRole('dialog', { name: 'Dodaj kategorię' });
    await expect(dialog.getByLabel('Nazwa kategorii')).toBeFocused();
    await dialog.getByLabel('Nazwa kategorii').fill(name);
    await dialog.getByLabel(/Slug/).fill('Zły Slug!');
    await dialog.getByLabel(/Slug/).blur();
    await expect(dialog.getByText(/Slug jest wymagany i może zawierać/)).toBeVisible();
    await dialog.getByLabel(/Slug/).fill(slug);
    await dialog.getByLabel('Kolejność').fill('999');
    await dialog.getByRole('button', { name: 'Dodaj' }).click();
    await expect(dialog).toBeHidden();

    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible();
    await expect(row).toContainText(slug);

    // edycja
    await row.getByRole('button', { name: `Edytuj kategorię ${name}` }).click();
    const edit = page.getByRole('dialog', { name: 'Edytuj kategorię' });
    await edit.getByLabel('Kolejność').fill('998');
    await edit.getByRole('button', { name: 'Zapisz' }).click();
    await expect(edit).toBeHidden();
    await expect(page.getByRole('row').filter({ hasText: name })).toContainText('998');

    // widoczna publicznie w filtrach
    const anon = await browser.newContext({ storageState: undefined });
    try {
      const pub = await anon.newPage();
      await pub.goto('/jobs');
      await expect(pub.getByRole('group', { name: 'Kategoria' }).getByRole('radio', { name: new RegExp(name) })).toBeAttached();
    } finally {
      await anon.close();
    }

    await scanA11y(page, 'panel admina – kategorie');
    await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: `Usuń kategorię ${name}` }).click();
    const confirm = page.getByRole('dialog');
    await confirm.getByRole('button', { name: /^Usuń/ }).click();
    await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(0);
  });
});

test.describe('admin – oferty, aplikacje i moderacja zdjęć', () => {
  test('zakładki Oferty i Aplikacje ładują dane, wyszukiwarka filtruje', async ({ page }) => {
    await openAdmin(page, 'Oferty');
    await expect(page.getByRole('status').filter({ hasText: /Znaleziono: \d+/ })).toBeVisible();
    await page.getByRole('textbox', { name: 'Szukaj' }).fill('zzzBrakTakiejOferty');
    await expect(page.getByText('Brak wyników')).toBeVisible();
    await page.getByRole('button', { name: 'Wyczyść filtry' }).first().click();
    await expect(page.getByRole('table', { name: 'Oferty pracy' })).toBeVisible();
    await scanA11y(page, 'panel admina – oferty');

    await page.getByRole('tab', { name: 'Aplikacje' }).click();
    await expect(page.getByRole('status').filter({ hasText: /Znaleziono: \d+ aplikacji/ })).toBeVisible();
  });

  test('moderacja: admin usuwa zdjęcie wgrane przez pracodawcę i tło ofert', async ({ page, browser }) => {
    // pracodawca wgrywa zdjęcie i tło
    const employer = await browser.newContext({ storageState: authFile('employer') });
    try {
      const ep = await employer.newPage();
      await ep.goto('/employer');
      await ep.getByRole('tab', { name: 'Profil firmy' }).click();
      await expect(ep.locator('li.tile.photo')).toHaveCount(0);
      await ep.locator('app-company-photos input[type=file]').setInputFiles({ name: 'moderacja.png', mimeType: 'image/png', buffer: makePng(320, 200, [90, 90, 90]) });
      await expect(ep.locator('li.tile.photo')).toHaveCount(1);
      await ep.locator('app-offer-background-settings input[type=file]').setInputFiles({ name: 'tlo.png', mimeType: 'image/png', buffer: makePng(800, 300, [30, 60, 90]) });
      await expect(ep.getByRole('button', { name: 'Usuń tło' })).toBeVisible();
    } finally {
      await employer.close();
    }

    await openAdmin(page, 'Firmy');
    await page.getByRole('textbox', { name: 'Szukaj' }).fill('TechCorp');
    const row = page.getByRole('row').filter({ hasText: 'TechCorp' }).first();
    const media = row.getByRole('button', { name: /^Zdjęcia i tło ofert:/ });
    await expect(media).toBeVisible();
    await media.click();
    const dialog = page.getByRole('dialog', { name: /Zdjęcia i tło/ });
    await expect(dialog).toBeVisible();
    await scanA11y(page, 'dialog moderacji zdjęć');
    await dialog.getByRole('button', { name: /^Usuń zdjęcie/ }).click();
    await page.getByRole('dialog', { name: /Usunąć/ }).getByRole('button', { name: /^Usuń/ }).click();
    await expect(dialog.getByText('Firma nie ma zdjęć.')).toBeVisible();
    const removeBackground = dialog.getByRole('button', { name: /Usuń tło/ });
    if (await removeBackground.count()) {
      await removeBackground.click();
      await page.getByRole('dialog', { name: /Usunąć/ }).getByRole('button', { name: /^Usuń/ }).click();
    }
    await dialog.getByRole('button', { name: 'Zamknij' }).click();
    await expect(dialog).toBeHidden();
  });
});
