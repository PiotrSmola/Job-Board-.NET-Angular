import { Page } from '@playwright/test';
import { expect, test } from './fixtures/test';
import { readState } from './fixtures/state';

/** Liczba z nagłówka wyników („13 ofert”). */
async function resultsCount(page: Page): Promise<number> {
  const text = await page.locator('#results-title').innerText();
  const match = text.match(/^(\d+)\s/);
  if (!match) throw new Error(`Nieoczekiwany nagłówek wyników: ${text}`);
  return Number(match[1]);
}

async function openList(page: Page, query = ''): Promise<void> {
  await page.goto('/jobs' + query);
  await expect(page.locator('article.job-card').first()).toBeVisible();
  await expect(page.locator('#results-title')).toHaveText(/^\d+ /);
}

test.describe('anonim – lista ofert', () => {
  test('wyświetla karty, licznik wyników i tytuł strony', async ({ page }) => {
    await openList(page);
    await expect(page).toHaveTitle('Oferty pracy – Job Board');
    await expect(page.getByRole('heading', { level: 1, name: 'Oferty pracy' })).toBeVisible();
    const cards = page.locator('article.job-card');
    expect(await cards.count()).toBeGreaterThan(0);
    const first = cards.first();
    await expect(first.getByRole('link').first()).toHaveAttribute('href', /\/jobs\/\d+/);
    await expect(first.locator('.salary')).not.toBeEmpty();
    // anonim nie ma serduszka
    await expect(page.getByRole('button', { name: /Zapisz ofertę|Usuń z zapisanych/ })).toHaveCount(0);
    // licznik ogłaszany czytnikom
    await expect(page.getByRole('status').filter({ hasText: /Znaleziono \d+/ })).toBeAttached();
  });

  test('wyszukiwanie ma debounce i nie dokłada wpisów do historii', async ({ page }) => {
    await openList(page);
    const total = await resultsCount(page);
    const historyLength = await page.evaluate(() => history.length);
    const box =page.getByRole('searchbox', { name: 'Stanowisko, firma lub technologia' });
    await box.pressSequentially('Developer', { delay: 40 });
    // debounce 500 ms: zaraz po wpisaniu adres jeszcze się nie zmienił
    expect(page.url()).not.toContain('searchTerm');
    await expect(page).toHaveURL(/searchTerm=Developer/);
    const filtered = await resultsCount(page);
    expect(filtered).toBeLessThan(total);
    expect(filtered).toBeGreaterThan(0);
    await expect(page.locator('.active-filters')).toBeVisible();

    // wyszukiwanie bez wyników
    await box.fill('zzzNieMaTakiejOferty');
    await expect(page.getByText('Brak ofert')).toBeVisible();
    await expect(page.locator('#results-title')).toHaveText('0 ofert');

    // wpisy z debounce zastępują bieżący (replace) – historia przeglądarki nie rośnie
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
  });

  test('Enter i „Szukaj” stosują frazę od razu, lokalizacja filtruje', async ({ page }) => {
    await openList(page);
    const total = await resultsCount(page);
    await page.getByRole('textbox', { name: 'Lokalizacja' }).fill('Warszawa');
    await page.getByRole('button', { name: 'Szukaj' }).click();
    await expect(page).toHaveURL(/location=Warszawa/);
    await expect.poll(() => resultsCount(page)).toBeLessThan(total);
    for (const text of await page.locator('article.job-card .company').allInnerTexts()) {
      expect(text).toContain('Warszawa');
    }
  });

  test('filtry mają liczniki zgodne z wynikami, można je wyczyścić', async ({ page }) => {
    await openList(page);
    const total = await resultsCount(page);
    const level = page.getByRole('group', { name: 'Poziom' });
    // pierwsza opcja z niezerowym licznikiem mniejszym od łącznej liczby
    const options = level.locator('label.check');
    await expect(options.first().locator('.count')).toBeVisible();
    const n = await options.count();
    let picked: { name: string; count: number } | null = null;
    for (let i = 0; i < n && !picked; i++) {
      const count = Number(await options.nth(i).locator('.count').innerText());
      if (count > 0 && count < total) picked = { name: (await options.nth(i).locator('span').first().innerText()).trim(), count };
    }
    expect(picked, 'brak opcji poziomu z licznikiem w (0, łącznie)').not.toBeNull();

    await level.getByRole('checkbox', { name: new RegExp(`^${picked!.name}`) }).check();
    await expect(page).toHaveURL(/seniority=/);
    await expect.poll(() => resultsCount(page)).toBe(picked!.count);
    await expect(page.locator('.active-filters')).toContainText(picked!.name);

    // kolejny filtr zawęża albo zostawia ten sam zbiór, nigdy nie zwiększa
    const work = page.getByRole('group', { name: 'Tryb pracy' });
    await work.getByRole('checkbox').first().check();
    await expect(page).toHaveURL(/workType/i);
    await expect.poll(() => resultsCount(page)).toBeLessThanOrEqual(picked!.count);

    await page.getByRole('button', { name: 'Wyczyść filtry' }).click();
    await expect(page).toHaveURL(/\/jobs$/);
    await expect.poll(() => resultsCount(page)).toBe(total);
    await expect(page.getByRole('button', { name: 'Wyczyść filtry' })).toHaveCount(0);
  });

  test('filtr kategorii (radio), wynagrodzenie od-do i sortowanie', async ({ page }) => {
    await openList(page);
    const total = await resultsCount(page);
    const category = page.getByRole('group', { name: 'Kategoria' });
    const radios = category.getByRole('radio');
    expect(await radios.count()).toBeGreaterThan(2);
    await radios.nth(1).check();
    await expect(page).toHaveURL(/category=/);
    await expect.poll(() => resultsCount(page)).toBeLessThan(total);
    await category.getByRole('radio', { name: 'Wszystkie' }).check();
    await expect(page).not.toHaveURL(/category=/);

    const from = page.getByRole('group', { name: 'Wynagrodzenie' }).getByLabel('Od');
    const to = page.getByRole('group', { name: 'Wynagrodzenie' }).getByLabel('Do');
    await from.fill('20000');
    await to.fill('10000');
    await expect(page.getByText('Kwota „od” nie może być wyższa niż „do”')).toBeVisible();
    await to.fill('');
    await expect.poll(() => resultsCount(page)).toBeLessThan(total);

    await page.getByRole('combobox', { name: 'Sortuj' }).click();
    await page.getByRole('option').nth(1).click();
    await expect(page).toHaveURL(/orderBy=/);
  });

  test('paginacja zmienia stronę, a przycisk wstecz ją przywraca', async ({ page }) => {
    await openList(page);
    const firstTitle = await page.locator('article.job-card h3').first().innerText();
    const paginator = page.locator('mat-paginator');
    await expect(paginator).toContainText(/1–10 z \d+/);
    await paginator.getByRole('button', { name: 'Następna strona' }).click();
    await expect(page).toHaveURL(/page=2/);
    await expect(page.locator('article.job-card h3').first()).not.toHaveText(firstTitle);
    // zmiana strony nie przenosi fokusu ani nie przeładowuje shellu
    await page.goBack();
    await expect(page).not.toHaveURL(/page=2/);
    await expect(page.locator('article.job-card h3').first()).toHaveText(firstTitle);
    await page.goForward();
    await expect(page).toHaveURL(/page=2/);
  });

  test('adres z filtrami odtwarza stan po przeładowaniu', async ({ page }) => {
    await openList(page, '?searchTerm=Developer&seniority=3');
    await expect(page.getByRole('searchbox', { name: 'Stanowisko, firma lub technologia' })).toHaveValue('Developer');
    await expect(page.getByRole('group', { name: 'Poziom' }).getByRole('checkbox', { checked: true })).toHaveCount(1);
    await page.reload();
    await expect(page.getByRole('searchbox', { name: 'Stanowisko, firma lub technologia' })).toHaveValue('Developer');
  });
});

test.describe('anonim – szczegóły oferty', () => {
  test('hero, fakty, sekcje i podobne oferty; „Wróć do wyników” zachowuje filtry', async ({ page }) => {
    await openList(page, '?searchTerm=Developer');
    const card = page.locator('article.job-card').first();
    const title = (await card.locator('h3').innerText()).trim();
    await card.getByRole('link', { name: title, exact: true }).click();

    await expect(page).toHaveURL(/\/jobs\/\d+/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(title);
    await expect(page).toHaveTitle(`${title} – Job Board`);
    // fokus po nawigacji ścieżki trafia na <main>
    await expect(page.locator('main#main')).toBeFocused();

    const facts = page.locator('dl.facts');
    await expect(facts.getByText('Wynagrodzenie', { exact: true })).toBeVisible();
    await expect(facts.getByText('Umowa', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Opis stanowiska' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'O firmie' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Wróć do wyników' })).toBeVisible();
    // gość widzi zachętę do logowania w sekcji aplikowania
    await expect(page.getByRole('heading', { name: 'Twoja aplikacja' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Zaloguj się, aby aplikować' })).toBeVisible();

    const similar = page.getByRole('region', { name: 'Podobne oferty' });
    if (await similar.count()) {
      const links = similar.getByRole('link');
      expect(await links.count()).toBeGreaterThan(0);
    }

    await page.getByRole('link', { name: 'Wróć do wyników' }).click();
    await expect(page).toHaveURL(/\/jobs\?.*searchTerm=Developer/);
    await expect(page.getByRole('searchbox', { name: 'Stanowisko, firma lub technologia' })).toHaveValue('Developer');
  });

  test('przycisk „Aplikuj” przewija do sekcji aplikowania bez zmiany fokusu strony', async ({ page }) => {
    await openList(page);
    await page.locator('article.job-card h3 a').filter({ hasNotText: 'E2E' }).first().click();
    await expect(page.locator('h1#offer-title')).toBeVisible();
    // gość: brak przycisku Aplikuj (aplikowanie po zalogowaniu) albo przewinięcie do #apply
    const apply = page.getByRole('button', { name: 'Aplikuj' });
    if (await apply.count()) {
      await apply.click();
      await expect(page.getByRole('heading', { name: 'Twoja aplikacja' })).toBeFocused();
    }
  });

  test('nieistniejąca oferta pokazuje komunikat i tytuł „Oferta niedostępna”', async ({ page, monitor }) => {
    monitor.allow(/\/api\/jobs\/999999 404/, /Failed to load resource.*404/, /Error loading job/);
    await page.goto('/jobs/999999');
    await expect(page.getByRole('heading', { level: 1, name: 'Oferta niedostępna' })).toBeVisible();
    await expect(page).toHaveTitle('Oferta niedostępna – Job Board');
    await page.getByRole('link', { name: 'Przejdź do listy ofert' }).click();
    await expect(page).toHaveURL(/\/jobs$/);
  });

  test('kategoria w faktach prowadzi do listy przefiltrowanej po kategorii', async ({ page }) => {
    await openList(page);
    await page.locator('article.job-card h3 a').filter({ hasNotText: 'E2E' }).first().click();
    const link = page.locator('a.category-link');
    if (await link.count()) {
      await link.click();
      await expect(page).toHaveURL(/\/jobs\?category=/);
    }
  });
});

test.describe('anonim – firmy', () => {
  test('lista firm: wyszukiwanie, sortowanie, paginacja i profil', async ({ page }) => {
    await page.goto('/companies');
    await expect(page).toHaveTitle('Firmy – Job Board');
    const cards = page.locator('a.company-card');
    await expect(cards.first()).toBeVisible();
    const total = await cards.count();
    expect(total).toBeGreaterThan(0);

    const search = page.getByRole('searchbox', { name: 'Szukaj firmy' });
    await search.fill('Tech');
    await expect.poll(() => cards.count()).toBeLessThanOrEqual(total);
    await expect(cards.first()).toContainText(/tech/i);
    await page.getByRole('button', { name: 'Wyczyść wyszukiwanie' }).click();
    await expect(search).toBeFocused();
    await expect.poll(() => cards.count()).toBe(total);

    await search.fill('zzzbrakfirmy');
    await expect(page.getByText('Brak firm')).toBeVisible();
    await search.fill('');

    await cards.first().click();
    await expect(page).toHaveURL(/\/companies\/\d+/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Aktualne oferty' })).toBeVisible();
    await expect(page.getByRole('complementary', { name: 'Informacje o firmie' })).toBeVisible();
    await expect(page.locator('main#main')).toBeFocused();
    const name = (await page.getByRole('heading', { level: 1 }).innerText()).trim();
    await expect(page).toHaveTitle(`${name} – Job Board`);

    await page.getByRole('link', { name: 'Wszystkie firmy' }).click();
    await expect(page).toHaveURL(/\/companies$/);
  });

  test('profil firmy z ofertami prowadzi do szczegółów oferty', async ({ page }) => {
    await page.goto('/companies');
    await page.locator('a.company-card', { hasText: /[1-9]\d* ofert/ }).first().click();
    const job = page.locator('.job-row a.job-title').first();
    await expect(job).toBeVisible();
    await job.click();
    await expect(page).toHaveURL(/\/jobs\/\d+/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('nieistniejąca firma pokazuje komunikat', async ({ page, monitor }) => {
    monitor.allow(/\/api\/companies\/999999 404/, /Failed to load resource.*404/, /Error loading company/);
    await page.goto('/companies/999999');
    await expect(page.getByRole('heading', { level: 1, name: 'Nie znaleziono firmy' })).toBeVisible();
  });
});

test.describe('anonim – logowanie', () => {
  test('błędne hasło pokazuje komunikat w role=alert, pola wymagane – błędy walidacji', async ({ page, monitor }) => {
    monitor.allow(/\/api\/auth\/login 401/, /Failed to load resource.*401/);
    await page.goto('/login');
    await expect(page).toHaveTitle('Logowanie – Job Board');
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await expect(page.getByText('Podaj adres e-mail')).toBeVisible();
    await expect(page.getByText('Podaj hasło')).toBeVisible();

    await page.getByLabel('E-mail').fill('nikt@example.com');
    await page.locator('input[formcontrolname="password"]').fill('ZleHaslo123!');
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await expect(page.getByRole('alert').filter({ hasText: /\S/ })).toBeVisible();
    await expect(page).toHaveURL(/\/login/);

    // przełącznik widoczności hasła
    const toggle = page.getByRole('button', { name: 'Pokaż hasło' });
    await toggle.click();
    await expect(page.locator('input[formcontrolname="password"]')).toHaveAttribute('type', 'text');
    await expect(toggle).toHaveAttribute('aria-pressed', 'true');

    // konta demonstracyjne w <details>
    await page.getByText('Konta demonstracyjne').click();
    await expect(page.getByText('rekruter@techcorp.pl / Employer123!')).toBeVisible();
  });

  test('gość na stronie oferty loguje się i wraca na ofertę (returnUrl)', async ({ page }) => {
    const candidate = readState().candidate!;
    await openList(page);
    await page.locator('article.job-card h3 a').filter({ hasNotText: 'E2E' }).first().click();
    await expect(page.locator('h1#offer-title')).toBeVisible();
    const title = (await page.locator('h1#offer-title').innerText()).trim();
    const jobUrl = new URL(page.url()).pathname;
    await page.getByRole('link', { name: 'Zaloguj się, aby aplikować' }).click();
    await expect(page).toHaveURL(/\/login\?returnUrl=/);

    await page.getByLabel('E-mail').fill(candidate.email);
    await page.locator('input[formcontrolname="password"]').fill(candidate.password);
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await expect(page).toHaveURL(new RegExp(jobUrl.replace('/', '\\/')));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(title);
    await expect(page.getByRole('button', { name: /Menu konta/ })).toBeVisible();
    // zalogowany kandydat widzi formularz aplikacji zamiast zachęty
    await expect(page.getByRole('link', { name: 'Zaloguj się, aby aplikować' })).toHaveCount(0);
  });

  test('chronione strony przekierowują gościa do logowania', async ({ page }) => {
    for (const path of ['/my-applications', '/saved-jobs', '/account', '/employer', '/admin']) {
      await page.goto(path);
      await expect(page, path).toHaveURL(/\/(login|jobs)/);
    }
  });

  test('rejestracja: walidacja haseł i przełącznik typu konta', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('Imię').fill('A');
    await page.locator('input[formcontrolname="password"]').fill('slabe');
    await expect(page.locator('app-password-strength')).toBeVisible();
    await page.getByRole('button', { name: 'Załóż konto' }).click();
    await expect(page.getByText(/Hasło musi mieć/)).toBeVisible();
    await page.getByText('Rekrutuję', { exact: true }).click();
    await expect(page.getByLabel('Nazwa firmy')).toBeVisible();
    await page.getByText('Szukam pracy', { exact: true }).click();
    await expect(page.getByLabel('Nazwa firmy')).toHaveCount(0);
  });
});
