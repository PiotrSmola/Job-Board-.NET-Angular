import { Page } from '@playwright/test';
import { expect, scanA11y, test } from './fixtures/test';
import { authFile } from './fixtures/state';

/** Szerokość zawartości nie może przekraczać okna (brak przewijania w poziomie – WCAG 1.4.10). */
async function expectNoHorizontalScroll(page: Page, label: string): Promise<void> {
  const overflow = await page.evaluate(() => ({
    doc: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    body: document.body.scrollWidth - document.documentElement.clientWidth
  }));
  expect(overflow.doc, `${label}: documentElement przewija się w poziomie`).toBeLessThanOrEqual(0);
  expect(overflow.body, `${label}: body przewija się w poziomie`).toBeLessThanOrEqual(0);
}

async function firstJobPath(page: Page): Promise<string> {
  await page.goto('/jobs');
  const link = page.locator('article.job-card h3 a').filter({ hasNotText: 'E2E' }).first();
  await expect(link).toBeVisible();
  return (await link.getAttribute('href'))!;
}

async function firstCompanyPath(page: Page): Promise<string> {
  await page.goto('/companies');
  const link = page.locator('a.company-card').first();
  await expect(link).toBeVisible();
  return (await link.getAttribute('href'))!;
}

test.describe('przekrojowo – gość', () => {
  test('skip link: pierwszy Tab, Enter przenosi fokus na <main>', async ({ page }) => {
    await page.goto('/jobs');
    await expect(page.locator('article.job-card').first()).toBeVisible();
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: 'Przejdź do treści' });
    await expect(skip).toBeFocused();
    // po sfokusowaniu link jest widoczny w oknie
    const box = await skip.boundingBox();
    expect(box && box.y >= 0 && box.y < 200).toBe(true);
    await page.keyboard.press('Enter');
    await expect(page.locator('main#main')).toBeFocused();
    // kolejny Tab trafia w treść, nie w nawigację
    await page.keyboard.press('Tab');
    const inMain = await page.evaluate(() => !!document.activeElement?.closest('main'));
    expect(inMain).toBe(true);
  });

  test('po zmianie ścieżki fokus trafia na <main> i tytuł strony jest aktualny; zmiana filtra go nie rusza', async ({ page }) => {
    await page.goto('/jobs');
    await expect(page.locator('article.job-card').first()).toBeVisible();
    const routes: [string, string][] = [['Firmy', 'Firmy – Job Board'], ['Logowanie', 'Logowanie – Job Board'], ['Rejestracja', 'Rejestracja – Job Board'], ['Oferty', 'Oferty pracy – Job Board']];
    for (const [link, title] of routes) {
      await page.getByRole('navigation', { name: 'Główna nawigacja' }).getByRole('link', { name: link }).click();
      await expect(page).toHaveTitle(title);
      await expect(page.locator('main#main')).toBeFocused();
    }
    // zmiana filtra (query params) nie przenosi fokusu z kontrolki
    await expect(page.locator('article.job-card').first()).toBeVisible();
    const radio = page.getByRole('group', { name: 'Poziom' }).getByRole('checkbox').first();
    await radio.check();
    await expect(page).toHaveURL(/seniority=/);
    await expect(radio).toBeFocused();
    // „Wyczyść filtry” – fokus nie ginie w <body>
    await page.getByRole('button', { name: 'Wyczyść filtry' }).click();
    await expect(page).toHaveURL(/\/jobs$/);
    const focusInBody = await page.evaluate(() => document.activeElement === document.body);
    expect(focusInBody, 'fokus po „Wyczyść filtry” wypadł na <body>').toBe(false);
  });

  test('fokus jest widoczny (obrys 2 px) na linkach nawigacji i polach', async ({ page }) => {
    await page.goto('/jobs');
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    const outline = await page.evaluate(() => {
      const style = getComputedStyle(document.activeElement!);
      return { width: parseFloat(style.outlineWidth), style: style.outlineStyle, color: style.outlineColor };
    });
    expect(outline.style).not.toBe('none');
    expect(outline.width).toBeGreaterThanOrEqual(2);
    // logo -> pierwsze linki nawigacji
    const names: string[] = [];
    for (let i = 0; i < 5; i++) {
      names.push(await page.evaluate(() => (document.activeElement as HTMLElement).textContent?.trim() ?? ''));
      await page.keyboard.press('Tab');
    }
    expect(names.join('|')).toMatch(/Job Board|Oferty|Firmy|Logowanie|Rejestracja/);
  });

  test('wymiar 375 px: brak przewijania w poziomie na głównych stronach', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    const job = await firstJobPath(page);
    const company = await firstCompanyPath(page);
    for (const path of ['/jobs', job, '/companies', company, '/login', '/register', '/register?type=employer', '/forgot-password']) {
      await page.goto(path);
      await expect(page.locator('main#main h1, main#main h2').first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      await expectNoHorizontalScroll(page, path);
    }
    // otwarte filtry na wąskim ekranie
    await page.goto('/jobs');
    await page.getByRole('button', { name: /^Filtry/ }).click();
    await expect(page.getByRole('group', { name: 'Poziom' })).toBeVisible();
    await expectNoHorizontalScroll(page, '/jobs z filtrami');
  });

  test('wymiar 320 px: reflow głównych stron publicznych', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 700 });
    const job = await firstJobPath(page);
    for (const path of ['/jobs', job, '/companies', '/login']) {
      await page.goto(path);
      await expect(page.locator('main#main h1, main#main h2').first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      await expectNoHorizontalScroll(page, `320px ${path}`);
    }
  });

  test('etykiety w nawigacji mobilnej pozostają dostępne (tylko ikony)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/jobs');
    const nav = page.getByRole('navigation', { name: 'Główna nawigacja' });
    for (const name of ['Oferty', 'Firmy', 'Logowanie', 'Rejestracja']) {
      await expect(nav.getByRole('link', { name })).toBeAttached();
      const box = await nav.getByRole('link', { name }).boundingBox();
      expect(box && box.width >= 24 && box.height >= 24, `cel dotyku „${name}” ≥ 24×24 px (WCAG 2.5.8)`).toBe(true);
    }
  });

  test('skan axe: strony publiczne (desktop)', async ({ page }) => {
    const job = await firstJobPath(page);
    const company = await firstCompanyPath(page);
    const pages: [string, string][] = [
      ['/jobs', 'lista ofert'], [job, 'szczegóły oferty'], ['/companies', 'lista firm'], [company, 'profil firmy'],
      ['/login', 'logowanie'], ['/register', 'rejestracja'], ['/register?type=employer', 'rejestracja pracodawcy'],
      ['/forgot-password', 'nie pamiętasz hasła']
    ];
    for (const [path, label] of pages) {
      await page.goto(path);
      await expect(page.locator('main#main h1, main#main h2').first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      await scanA11y(page, label);
    }
    // lista ofert z aktywnymi filtrami i otwartym menu sortowania
    await page.goto('/jobs?searchTerm=Developer&seniority=3');
    await expect(page.locator('article.job-card').first()).toBeVisible();
    await scanA11y(page, 'lista ofert z filtrami');
    await page.getByRole('combobox', { name: 'Sortuj' }).click();
    await expect(page.getByRole('listbox')).toBeVisible();
    await scanA11y(page, 'lista ofert – otwarte sortowanie');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('combobox', { name: 'Sortuj' })).toBeFocused();
  });

  test('skan axe: strony publiczne (375 px)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    const job = await firstJobPath(page);
    for (const [path, label] of [['/jobs', 'lista ofert 375'], [job, 'szczegóły oferty 375'], ['/companies', 'lista firm 375'], ['/login', 'logowanie 375']] as const) {
      await page.goto(path);
      await expect(page.locator('main#main h1, main#main h2').first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      await scanA11y(page, label);
    }
  });

  test('tytuły stron dla wszystkich tras publicznych', async ({ page }) => {
    const expected: [string, string][] = [
      ['/jobs', 'Oferty pracy – Job Board'], ['/companies', 'Firmy – Job Board'], ['/login', 'Logowanie – Job Board'],
      ['/register', 'Rejestracja – Job Board'], ['/forgot-password', 'Nie pamiętasz hasła? – Job Board'],
      ['/reset-password', 'Ustaw nowe hasło – Job Board'], ['/confirm-email', 'Potwierdzenie adresu e-mail – Job Board']
    ];
    for (const [path, title] of expected) {
      await page.goto(path);
      await expect(page, path).toHaveTitle(title);
    }
    // jeden <h1> na stronie i lang="pl"
    await page.goto('/jobs');
    await expect(page.locator('html')).toHaveAttribute('lang', 'pl');
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  });
});

test.describe('przekrojowo – kandydat', () => {
  test.use({ storageState: authFile('candidate') });

  test('menu konta: klawiatura, Escape i powrót fokusu', async ({ page }) => {
    await page.goto('/jobs');
    const account = page.getByRole('button', { name: /Menu konta/ });
    await account.focus();
    await page.keyboard.press('Enter');
    const menu = page.getByRole('menu');
    await expect(menu).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: 'Moje konto' })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: 'Wyloguj' })).toBeVisible();
    // pierwszy fokusowalny element menu to „Moje konto” (wiersz z e-mailem jest wyłączony)
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Escape');
    await expect(menu).toBeHidden();
    await expect(account).toBeFocused();

    await account.click();
    await page.getByRole('menuitem', { name: 'Moje konto' }).click();
    await expect(page).toHaveURL(/\/account/);
    await expect(page.locator('main#main')).toBeFocused();
  });

  test('nawigacja kandydata ma aria-current i widoczne etykiety', async ({ page }) => {
    await page.goto('/my-applications');
    const nav = page.getByRole('navigation', { name: 'Główna nawigacja' });
    await expect(nav.getByRole('link', { name: 'Moje aplikacje' })).toHaveAttribute('aria-current', 'page');
    await expect(nav.getByRole('link', { name: 'Zapisane' })).not.toHaveAttribute('aria-current', 'page');
    await expect(nav.getByRole('link', { name: 'Panel pracodawcy' })).toHaveCount(0);
  });

  test('wymiar 375 px: strony kandydata bez przewijania w poziomie', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    for (const path of ['/saved-jobs', '/my-applications', '/account', '/account?tab=documents', '/account?tab=security']) {
      await page.goto(path);
      await expect(page.locator('main#main h1').first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      await expectNoHorizontalScroll(page, path);
    }
  });

  test('skan axe: lista ofert i szczegóły oferty dla zalogowanego kandydata', async ({ page }) => {
    const job = await firstJobPath(page);
    await scanA11y(page, 'lista ofert (kandydat)');
    await page.goto(job);
    await expect(page.locator('h1#offer-title')).toBeVisible();
    await expect(page.locator('#apply form')).toBeVisible();
    await scanA11y(page, 'szczegóły oferty z formularzem aplikacji');
  });

  test('chronione trasy pracodawcy i admina nie są dostępne dla kandydata', async ({ page }) => {
    for (const path of ['/employer', '/admin']) {
      await page.goto(path);
      await expect(page, path).not.toHaveURL(new RegExp(`${path}$`));
    }
  });
});

test.describe('przekrojowo – pracodawca', () => {
  test.use({ storageState: authFile('employer') });

  test('wymiar 375 px: panel pracodawcy (wszystkie zakładki) bez przewijania w poziomie', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/employer');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    for (const tab of ['Oferty', 'Aplikacje', 'Profil firmy', 'Statystyki']) {
      await page.getByRole('tab', { name: tab }).click();
      await page.waitForLoadState('networkidle');
      // tabela może przewijać się we własnym kontenerze, ale nie cała strona
      await expectNoHorizontalScroll(page, `panel pracodawcy / ${tab}`);
    }
  });

  test('skan axe: zakładki panelu pracodawcy', async ({ page }) => {
    await page.goto('/employer');
    for (const tab of ['Aplikacje', 'Profil firmy', 'Statystyki']) {
      await page.getByRole('tab', { name: tab }).click();
      await page.waitForLoadState('networkidle');
      await scanA11y(page, `panel pracodawcy – ${tab}`);
    }
  });
});

test.describe('przekrojowo – admin', () => {
  test.use({ storageState: authFile('admin') });

  test('wymiar 375 px: panel admina (wszystkie zakładki) bez przewijania w poziomie', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/admin');
    await expect(page.getByRole('heading', { level: 1, name: 'Panel admina' })).toBeVisible();
    for (const tab of ['Statystyki', 'Oferty', 'Aplikacje', 'Firmy', 'Kategorie']) {
      await page.getByRole('tab', { name: tab }).click();
      await page.waitForLoadState('networkidle');
      await expectNoHorizontalScroll(page, `panel admina / ${tab}`);
    }
  });

  test('zakładki panelu admina działają z klawiatury (strzałki, Home, End)', async ({ page }) => {
    await page.goto('/admin');
    const tabs = page.getByRole('tablist', { name: 'Sekcje panelu' });
    await tabs.getByRole('tab', { name: 'Statystyki' }).focus();
    await page.keyboard.press('End');
    await expect(tabs.getByRole('tab', { name: 'Kategorie' })).toBeFocused();
    await page.keyboard.press('Home');
    await expect(tabs.getByRole('tab', { name: 'Statystyki' })).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Enter');
    await expect(tabs.getByRole('tab', { name: 'Oferty' })).toHaveAttribute('aria-selected', 'true');
  });
});
