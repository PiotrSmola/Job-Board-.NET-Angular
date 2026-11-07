import { expect, scanA11y, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

test.use({ storageState: authFile('candidate') });

test.describe('kandydat – po decyzji rekrutera', () => {
  test('widzi zmianę statusu i komentarz w historii aplikacji', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/my-applications');
    const article = page.locator('article.application', { hasText: job.title });
    await expect(article.getByText('Rozmowa').first()).toBeVisible();
    await article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` }).click();
    await expect(article.getByText('Zapraszamy na rozmowę w czwartek o 10:00')).toBeVisible();
    await expect(article.getByRole('heading', { level: 3, name: 'Historia' })).toBeVisible();

    // na stronie oferty status też jest aktualny
    await page.goto(`/jobs/${job.id}`);
    await expect(page.locator('#apply').getByText('Rozmowa')).toBeVisible();
  });

  test('wycofanie aplikacji przez okno potwierdzenia: Anuluj, Escape i potwierdzenie', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/my-applications');
    const article = page.locator('article.application', { hasText: job.title });
    await article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` }).click();
    const withdraw = article.getByRole('button', { name: new RegExp(`Wycofaj aplikację\\s*: ${job.title}`) });

    // Anuluj – nic się nie dzieje, fokus wraca na przycisk
    await withdraw.click();
    let dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Anuluj' })).toBeFocused();
    await scanA11y(page, 'dialog potwierdzenia wycofania');
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(withdraw).toBeFocused();

    // Escape zamyka okno bez wycofania
    await withdraw.click();
    dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(withdraw).toBeFocused();
    await expect(article.getByText('Rozmowa').first()).toBeVisible();

    // potwierdzenie
    await withdraw.click();
    dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: /^Wycofaj/ }).click();
    await expect(dialog).toBeHidden();

    const tabs = page.getByRole('tablist', { name: 'Filtr aplikacji' });
    await expect(tabs.getByRole('tab', { name: /W toku \(0\)/ })).toBeVisible();
    await tabs.getByRole('tab', { name: /Zakończone \(1\)/ }).click();
    const closed = page.locator('article.application', { hasText: job.title });
    await expect(closed.getByText('Wycofana').first()).toBeVisible();
    // wycofanej aplikacji nie wycofuje się ponownie
    const toggle = closed.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` });
    if ((await toggle.getAttribute('aria-expanded')) !== 'true') await toggle.click();
    await expect(closed.getByRole('button', { name: /Wycofaj aplikację/ })).toHaveCount(0);
    // załączniki pozostają dostępne
    await expect(closed.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ })).toBeVisible();
  });

  test('po wycofaniu strona oferty pozwala złożyć aplikację ponownie', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto(`/jobs/${job.id}`);
    const section = page.locator('#apply');
    await expect(section.getByText(/Wycofałeś wcześniej tę aplikację/)).toBeVisible();
    await expect(section.getByRole('button', { name: 'Aplikuj ponownie' })).toBeVisible();
  });
});
