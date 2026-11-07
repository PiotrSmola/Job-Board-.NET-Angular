import { Page } from '@playwright/test';
import { expect, test, scanA11y } from './fixtures/test';
import { authFile, readState, writeState } from './fixtures/state';

test.use({ storageState: authFile('employer') });

const DESCRIPTION = 'Opis oferty testowej E2E: pracujemy nad produktem webowym w Angularze i .NET, dbamy o dostępność i jakość kodu.';

async function openOffersTab(page: Page): Promise<void> {
  await page.goto('/employer');
  await expect(page.getByRole('tab', { name: 'Oferty' })).toHaveAttribute('aria-selected', 'true');
}

test.describe('pracodawca – panel i oferty', () => {
  test('panel ma zakładki, nagłówek firmy i można je przełączać z klawiatury', async ({ page }) => {
    await page.goto('/employer');
    await expect(page).toHaveTitle('Panel pracodawcy – Job Board');
    await expect(page.getByRole('heading', { level: 1 })).toContainText('TechCorp');
    await expect(page.getByRole('link', { name: 'Zobacz profil publiczny' })).toBeVisible();

    const tabs = page.getByRole('tablist', { name: 'Sekcje panelu' });
    for (const name of ['Oferty', 'Aplikacje', 'Profil firmy', 'Statystyki']) {
      await expect(tabs.getByRole('tab', { name })).toBeVisible();
    }
    await tabs.getByRole('tab', { name: 'Oferty' }).focus();
    await page.keyboard.press('ArrowRight');
    await expect(tabs.getByRole('tab', { name: 'Aplikacje' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(tabs.getByRole('tab', { name: 'Aplikacje' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('status').filter({ hasText: /Znaleziono: \d+ aplikacji/ })).toBeVisible();

    await tabs.getByRole('tab', { name: 'Statystyki' }).click();
    await expect(page.getByRole('region', { name: 'Statystyki' })).toBeVisible();
    await tabs.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(page.getByRole('heading', { level: 2, name: 'Dane firmy' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Zdjęcia' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Tło ofert' })).toBeVisible();
  });

  test('tworzy szkic oferty, nie widać go publicznie, potem publikuje', async ({ page, browser, request }) => {
    const runId = readState().runId!;
    const title = `E2E Frontend Developer ${runId}`;
    await openOffersTab(page);

    await page.getByRole('button', { name: 'Nowa oferta' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nowa oferta' });
    await expect(dialog).toBeVisible();
    // fokus startuje w polu tytułu, a przycisk zapisu jest zablokowany do czasu poprawnego formularza
    await expect(dialog.getByLabel('Tytuł stanowiska')).toBeFocused();
    await expect(dialog.getByRole('button', { name: 'Dodaj ofertę' })).toBeDisabled();

    await dialog.getByLabel('Tytuł stanowiska').fill(title);
    await dialog.getByLabel('Lokalizacja').fill('Rzeszów');
    await dialog.getByRole('combobox', { name: 'Poziom' }).click();
    await page.getByRole('option', { name: 'Mid' }).click();
    await dialog.getByRole('checkbox', { name: 'B2B' }).check();
    await dialog.getByLabel('Wynagrodzenie od').fill('12000');
    await dialog.getByLabel('Wynagrodzenie do').fill('9000');
    await expect(dialog.getByRole('alert').filter({ hasText: /nie może być wyższe/ })).toBeVisible();
    await dialog.getByLabel('Wynagrodzenie do').fill('18000');
    await dialog.getByLabel('Opis stanowiska').fill('za krótki');
    await dialog.getByLabel('Opis stanowiska').blur();
    await expect(dialog.getByText('Opis musi mieć minimum 50 znaków')).toBeVisible();
    await dialog.getByLabel('Opis stanowiska').fill(DESCRIPTION);
    await dialog.getByRole('combobox', { name: 'Status' }).click();
    await page.getByRole('option', { name: 'Szkic' }).click();
    await dialog.getByRole('button', { name: 'Dodaj ofertę' }).click();
    await expect(dialog).toBeHidden();
    // po zamknięciu dialogu fokus wraca na przycisk, który go otworzył
    await expect(page.getByRole('button', { name: 'Nowa oferta' })).toBeFocused();

    const row = page.getByRole('row').filter({ hasText: title });
    await expect(row).toBeVisible();
    await expect(row.getByText('Szkic')).toBeVisible();
    await expect(row).toContainText('12 000');
    writeState({ offer: { title } });

    // szkic nie jest widoczny dla anonima
    const anon = await browser.newContext({ storageState: undefined });
    const anonPage = await anon.newPage();
    await anonPage.goto(`/jobs?searchTerm=${encodeURIComponent(runId)}`);
    await expect(anonPage.getByText('Brak ofert')).toBeVisible();
    await anon.close();

    // publikacja szybką akcją
    await row.getByRole('button', { name: `Opublikuj: ${title}` }).click();
    await expect(page.getByRole('row').filter({ hasText: title }).getByText('Aktywna')).toBeVisible();
    await expect(page.locator('simple-snack-bar, .mat-mdc-snack-bar-label').first()).toContainText('Aktywna');

    const anon2 = await browser.newContext({ storageState: undefined });
    const publicPage = await anon2.newPage();
    await publicPage.goto(`/jobs?searchTerm=${encodeURIComponent(runId)}`);
    const card = publicPage.locator('article.job-card', { hasText: title });
    await expect(card).toBeVisible();
    await card.getByRole('link', { name: title }).click();
    await expect(publicPage.getByRole('heading', { level: 1 })).toHaveText(title);
    const id = Number(new URL(publicPage.url()).pathname.split('/').pop());
    writeState({ offer: { title, id } });
    await expect(publicPage.locator('dl.facts')).toContainText('12 000');
    await expect(publicPage.locator('dl.facts')).toContainText('B2B');
    await expect(publicPage.locator('dl.facts')).toContainText('Mid');
    await expect(publicPage.getByText(DESCRIPTION)).toBeVisible();
    await anon2.close();
    expect(request).toBeTruthy();
  });

  test('wyszukiwarka i filtry w tabeli ofert, edycja oferty', async ({ page }) => {
    const { title } = readState().offer!;
    await openOffersTab(page);
    await page.getByRole('textbox', { name: 'Szukaj' }).fill(title);
    await expect(page.getByRole('status').filter({ hasText: /Znaleziono: 1 / })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Wyczyść filtry' })).toBeVisible();

    await page.getByRole('button', { name: `Edytuj ofertę: ${title}` }).click();
    const dialog = page.getByRole('dialog', { name: 'Edytuj ofertę' });
    await expect(dialog.getByLabel('Tytuł stanowiska')).toHaveValue(title);
    // fokus początkowy ustawia się po animacji – dopiero wtedy wpisujemy w inne pole
    await expect(dialog.getByLabel('Tytuł stanowiska')).toBeFocused();
    await dialog.getByLabel('Lokalizacja').fill('Kraków');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row').filter({ hasText: title })).toContainText('Kraków');

    // Escape zamyka dialog i zwraca fokus
    await page.getByRole('button', { name: `Edytuj ofertę: ${title}` }).click();
    await expect(page.getByRole('dialog', { name: 'Edytuj ofertę' })).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Anuluj' }).click();
    await expect(page.getByRole('button', { name: `Edytuj ofertę: ${title}` })).toBeFocused();

    await page.getByRole('button', { name: 'Wyczyść filtry' }).click();
    await expect(page.getByRole('textbox', { name: 'Szukaj' })).toHaveValue('');
  });

  test('skan dostępności panelu pracodawcy i dialogu oferty', async ({ page }) => {
    await openOffersTab(page);
    await scanA11y(page, 'panel pracodawcy – oferty');
    await page.getByRole('button', { name: 'Nowa oferta' }).click();
    await expect(page.getByRole('dialog', { name: 'Nowa oferta' })).toBeVisible();
    await scanA11y(page, 'dialog nowej oferty');
    await page.keyboard.press('Escape');
  });
});
