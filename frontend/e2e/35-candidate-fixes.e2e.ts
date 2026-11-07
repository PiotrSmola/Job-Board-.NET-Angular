import { BrowserMonitor, expect, makePdf, scanA11yStrict, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

/**
 * Kandydat – weryfikacja poprawek odświeżonego UI (po 30, przed 40): nazwy przycisków w Moich aplikacjach,
 * potwierdzenie usunięcia załącznika i dokumentu, link „Moim koncie” z sekcji aplikowania, wolne ładowanie
 * sekcji konta (?tab=), axe na stronach zalogowanego użytkownika i formaty dat.
 * Kolejność testów ma znaczenie: dokument profilu jest usuwany przed sprawdzeniem linku „Moim koncie”.
 */
test.use({ storageState: authFile('candidate') });
test.describe.configure({ mode: 'serial' });


test.describe('kandydat – poprawki UI', () => {
  test('Moje aplikacje: nazwy przycisków bez spacji przed dwukropkiem', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/my-applications');
    const article = page.locator('article.application', { hasText: job.title });
    await article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` }).click();
    await expect(article.getByRole('button', { name: `Dodaj załączniki: ${job.title}`, exact: true })).toBeVisible();
    await expect(article.getByRole('button', { name: `Wycofaj aplikację: ${job.title}`, exact: true })).toBeVisible();
    await expect(article.locator('button.add-attachments')).toHaveAttribute('aria-label', `Dodaj załączniki: ${job.title}`);
    await expect(article.locator('button.withdraw')).toHaveAttribute('aria-label', `Wycofaj aplikację: ${job.title}`);
    // dostępna nazwa nie zawiera pojedynczej spacji przed dwukropkiem (nazwa liczona przez przeglądarkę)
    const names = await article.locator('button.add-attachments, button.withdraw').evaluateAll(buttons =>
      buttons.map(button => (button.getAttribute('aria-label') ?? button.textContent ?? '').trim()));
    for (const name of names) expect(name).not.toMatch(/\s:/);
  });

  test('Moje aplikacje: usunięcie załącznika pyta w oknie dialogowym (Anuluj / Usuń)', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/my-applications');
    const article = page.locator('article.application', { hasText: job.title });
    await article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` }).click();

    // osobny załącznik tylko do tego testu – pozostałe pliki aplikacji są potrzebne w 40 i 45
    await article.getByRole('button', { name: `Dodaj załączniki: ${job.title}` }).click();
    const addDialog = page.getByRole('dialog');
    await addDialog.locator('input[type=file]').setInputFiles({ name: 'dodatek-e2e.pdf', mimeType: 'application/pdf', buffer: makePdf('dodatek') });
    await addDialog.getByRole('button', { name: 'Prześlij' }).click();
    await expect(addDialog).toBeHidden({ timeout: 30_000 });
    const download = article.getByRole('button', { name: /Pobierz .*: dodatek-e2e\.pdf/ });
    await expect(download).toBeVisible();

    const remove = article.getByRole('button', { name: /Usuń .*: dodatek-e2e\.pdf/ });
    await remove.click();
    let dialog = page.getByRole('dialog', { name: 'Usunąć załącznik?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(download).toBeVisible();

    await remove.click();
    dialog = page.getByRole('dialog', { name: 'Usunąć załącznik?' });
    await dialog.getByRole('button', { name: 'Usuń' }).click();
    await expect(dialog).toBeHidden();
    await expect(download).toHaveCount(0);
    // pozostałe załączniki zostają
    await expect(article.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ })).toBeVisible();
    await expect(article.getByRole('button', { name: /Pobierz .*: list-motywacyjny\.pdf/ })).toBeVisible();
  });

  test('Moje konto: usunięcie dokumentu pyta w oknie dialogowym (Anuluj / Usuń)', async ({ page }) => {
    await page.goto('/account?tab=documents');
    const section = page.locator('#dokumenty');
    const download = section.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ });
    await expect(download).toBeVisible();
    const remove = section.getByRole('button', { name: /Usuń .*: cv-e2e\.pdf/ });

    await remove.click();
    let dialog = page.getByRole('dialog', { name: 'Usunąć dokument?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(download).toBeVisible();
    await page.reload();
    await expect(page.locator('#dokumenty').getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ })).toBeVisible();

    await page.locator('#dokumenty').getByRole('button', { name: /Usuń .*: cv-e2e\.pdf/ }).click();
    dialog = page.getByRole('dialog', { name: 'Usunąć dokument?' });
    await dialog.getByRole('button', { name: 'Usuń' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('#dokumenty').getByText('Nie masz jeszcze dokumentów w profilu')).toBeVisible();
    await page.reload();
    await expect(page.locator('#dokumenty').getByText('Nie masz jeszcze dokumentów w profilu')).toBeVisible();
  });

  test('strona oferty: link „Moim koncie” otwiera konto w nowej karcie, formularz zostaje', async ({ page, context, request }) => {
    const offerId = readState().offer!.id!;
    const response = await request.get('/api/jobs?pageSize=10');
    const jobs = await response.json() as { id: number; title: string }[];
    const other = jobs.find(job => job.id !== offerId)!;
    await page.goto(`/jobs/${other.id}`);
    await page.getByRole('button', { name: 'Aplikuj' }).click();
    const section = page.locator('#apply');
    await expect(section.getByRole('heading', { name: 'Twoja aplikacja' })).toBeFocused();
    await section.getByLabel(/Wiadomość do rekrutera/).fill('Wpisana wiadomość, której nie wolno stracić');

    const link = section.getByRole('link', { name: /Moim koncie/ });
    await expect(link).toBeVisible();
    await expect(link).toHaveAttribute('target', '_blank');
    await expect(link).toHaveAttribute('rel', /noopener/);

    const popupMonitor = new BrowserMonitor();
    context.on('page', popup => popupMonitor.attach(popup));
    const [popup] = await Promise.all([context.waitForEvent('page'), link.click()]);
    await popup.waitForLoadState('domcontentloaded');
    await expect(popup).toHaveURL(/\/account\?tab=documents/);
    await expect(popup.getByRole('heading', { level: 2, name: 'Dokumenty' })).toBeFocused();
    expect(await popup.evaluate(() => window.opener)).toBeNull();

    // oryginalna karta: ten sam adres i niezmieniony formularz
    await expect(page).toHaveURL(new RegExp(`/jobs/${other.id}$`));
    await expect(section.getByLabel(/Wiadomość do rekrutera/)).toHaveValue('Wpisana wiadomość, której nie wolno stracić');
    await expect(section.getByRole('heading', { name: 'Twoja aplikacja' })).toBeVisible();
    await popup.waitForTimeout(500);
    expect(popupMonitor.issues, 'konsola/żądania w nowej karcie').toEqual([]);
    await popup.close();
  });

  for (const [tab, anchor, heading] of [
    ['documents', '#dokumenty', 'Dokumenty'],
    ['notifications', '#powiadomienia', 'Powiadomienia']
  ] as const) {
    test(`konto ?tab=${tab} przy wolnym ładowaniu sekcji: właściwa sekcja widoczna i z fokusem`, async ({ page }) => {
      const delays: [RegExp, number][] = [[/\/api\/account\/profile/, 2500], [/\/api\/account\/documents/, 1200], [/\/api\/account\/notifications/, 1800]];
      await page.route('**/api/account/**', async route => {
        const delay = delays.find(([pattern]) => pattern.test(route.request().url()))?.[1] ?? 0;
        if (route.request().method() === 'GET' && delay) await new Promise(resolve => setTimeout(resolve, delay));
        await route.continue().catch(() => undefined);
      });
      await page.goto(`/account?tab=${tab}`);
      const h2 = page.locator(`${anchor} h2`);
      await expect(h2).toHaveText(heading);
      await expect(h2).toBeFocused();
      await expect(h2).toBeInViewport();
      // po dociągnięciu wszystkich sekcji (najwolniejsza: 2,5 s) sekcja nadal jest na ekranie i ma fokus
      await page.waitForTimeout(3500);
      await expect(page.locator('.skeleton, [aria-busy="true"]')).toHaveCount(0);
      await expect(h2).toBeFocused();
      await expect(h2).toBeInViewport();
      const box = (await h2.boundingBox())!;
      expect(box.y, 'nagłówek sekcji nie wypada poza górną krawędź').toBeGreaterThanOrEqual(0);
      expect(box.y, 'nagłówek sekcji w oknie przeglądarki').toBeLessThan(900);
    });
  }

  test('axe (WCAG 2.2 AA + label-content-name-mismatch) na stronach zalogowanego kandydata', async ({ page }) => {
    const pages: [string, string][] = [
      ['/jobs', 'lista ofert'],
      ['/saved-jobs', 'zapisane'],
      ['/my-applications', 'moje aplikacje'],
      ['/account', 'moje konto'],
      ['/companies', 'lista firm']
    ];
    for (const [url, label] of pages) {
      await page.goto(url);
      await expect(page.getByRole('button', { name: /Menu konta/ })).toBeVisible();
      await page.waitForLoadState('networkidle');
      const violations = await scanA11yStrict(page, `kandydat: ${label}`);
      expect(violations.filter(v => v.id === 'label-content-name-mismatch'), `label-content-name-mismatch na ${url}`).toEqual([]);
    }
  });

  test('daty: profil i historia aplikacji w formacie dd.MM.yyyy, bez błędów konsoli', async ({ page }) => {
    const job = readState().appliedJob!;
    await page.goto('/account');
    const created = page.locator('#profil dd').filter({ hasText: /^\d{2}\.\d{2}\.\d{4}$/ });
    await expect(created).toHaveCount(1);
    await page.goto('/my-applications');
    const article = page.locator('article.application', { hasText: job.title });
    await article.getByRole('button', { name: `Szczegóły aplikacji: ${job.title}` }).click();
    await expect(article.locator('time.date').first()).toHaveText(/^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$/);
  });
});
