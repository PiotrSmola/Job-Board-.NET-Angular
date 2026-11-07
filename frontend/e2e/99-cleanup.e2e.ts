import { expect, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

/**
 * Sprzątanie danych testowych (uruchamiane jako ostatnie): oferty „E2E …” (admin usuwa je razem z aplikacjami),
 * konto kandydata testowego, ewentualne zdjęcia i tło firmy TechCorp po przerwanych testach.
 */
test.describe('sprzątanie po testach E2E', () => {
  test.describe('admin', () => {
    test.use({ storageState: authFile('admin') });

    test('usuwa oferty testowe razem z aplikacjami', async ({ page }) => {
      await page.goto('/admin');
      await page.getByRole('tab', { name: 'Oferty' }).click();
      await page.getByRole('textbox', { name: 'Szukaj' }).fill('E2E Frontend Developer');
      await expect(page.getByRole('status').filter({ hasText: /Znaleziono: \d+/ })).toBeVisible();
      // wyszukiwarka ma debounce – lista przerysowuje się po chwili, a kliknięcie w odłączony przycisk kończyło się timeoutem
      await page.waitForTimeout(800);
      await page.waitForLoadState('networkidle');
      let guard = 0;
      while (guard++ < 10) {
        const delete_ = page.getByRole('button', { name: /^Usuń ofertę: E2E Frontend Developer/ }).first();
        if (!(await delete_.count())) break;
        const count = await page.getByRole('button', { name: /^Usuń ofertę: E2E Frontend Developer/ }).count();
        await delete_.click();
        await page.getByRole('dialog', { name: /Usunąć ofertę/ }).getByRole('button', { name: 'Usuń' }).click();
        await expect(page.getByRole('button', { name: /^Usuń ofertę: E2E Frontend Developer/ })).toHaveCount(count - 1);
      }
      await expect(page.getByRole('button', { name: /^Usuń ofertę: E2E Frontend Developer/ })).toHaveCount(0);
    });

    test('firma TechCorp nie ma zdjęć ani tła po testach', async ({ page, request }) => {
      await page.goto('/admin');
      await page.getByRole('tab', { name: 'Firmy' }).click();
      await page.getByRole('textbox', { name: 'Szukaj' }).fill('TechCorp');
      await expect(page.getByRole('row').filter({ hasText: 'TechCorp' }).first()).toBeVisible();
      const response = await request.get('/api/companies/1/photos');
      expect(response.ok()).toBe(true);
      expect(await response.json()).toEqual([]);
    });
  });

  test.describe('kandydat', () => {
    test.use({ storageState: authFile('candidate') });

    test('usuwa konto testowego kandydata', async ({ page }) => {
      const candidate = readState().candidate!;
      await page.goto('/account?tab=security');
      const section = page.locator('#haslo');
      await section.getByRole('button', { name: /Usuń konto/ }).click();
      await section.locator('input[formcontrolname="password"]').fill(candidate.password);
      await section.getByRole('button', { name: 'Usuń konto na stałe' }).click();
      await page.getByRole('dialog', { name: 'Usunąć konto?' }).getByRole('button', { name: 'Usuń konto' }).click();
      // po usunięciu użytkownik jest wylogowany
      await expect(page).toHaveURL(/\/(login|jobs)/);
      await expect(page.getByRole('link', { name: 'Logowanie' })).toBeVisible();
    });
  });
});
