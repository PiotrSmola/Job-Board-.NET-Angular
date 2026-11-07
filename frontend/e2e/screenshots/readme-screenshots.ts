import { execFileSync } from 'node:child_process';
import { mkdirSync, renameSync, rmSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { Browser, BrowserContext, Locator, Page, expect, test } from '@playwright/test';
import { ADMIN, EMPLOYER } from '../fixtures/state';

/**
 * Zrzuty ekranu do README (docs/screenshots/*.png). Działa na uruchomionym stacku z danymi demo:
 * firma TechCorp Solutions (zdjęcia, tło ofert, oferty), kandydat Jan Kowalski (aplikacje w różnych statusach,
 * zapisane oferty, dokument w profilu). Uruchomienie: `npm run screenshots` (osobna konfiguracja,
 * nie wchodzi w skład `npm run e2e`). Logowanie raz na rolę (limit 10/min na IP), potem współdzielone sesje.
 */
const OUT_DIR = resolve(__dirname, '..', '..', '..', 'docs', 'screenshots');
const MAILPIT_URL = process.env['E2E_MAILPIT_URL'] ?? 'http://mailpit:8025';
const CANDIDATE = { email: 'jan.kowalski@example.com', password: 'User123!' };

const DESKTOP = { width: 1440, height: 900 };
const MOBILE = { width: 390, height: 844 };
/** Maksymalna wysokość zrzutu pełnej strony (px) – dłuższe strony są przycinane. */
const MAX_HEIGHT = 1500;

type Role = 'candidate' | 'employer' | 'admin';
type StorageState = Awaited<ReturnType<BrowserContext['storageState']>>;

const sessions: Partial<Record<Role, StorageState>> = {};

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser, baseURL }) => {
  mkdirSync(OUT_DIR, { recursive: true });
  const credentials: Record<Role, { email: string; password: string }> = { candidate: CANDIDATE, employer: EMPLOYER, admin: ADMIN };
  for (const role of Object.keys(credentials) as Role[]) {
    const context = await browser.newContext({ baseURL, viewport: DESKTOP, locale: 'pl-PL' });
    const page = await context.newPage();
    await page.goto('/login');
    await page.getByLabel('E-mail').fill(credentials[role].email);
    await page.locator('input[formcontrolname="password"]').fill(credentials[role].password);
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await expect(page.getByRole('button', { name: /Menu konta/ })).toBeVisible();
    sessions[role] = await context.storageState();
    await context.close();
  }
});

/** Nowa strona w kontekście danej roli (albo anonimowym) – domyślnie desktop 1440×900, mobile 390×844. */
async function open(browser: Browser, baseURL: string | undefined, role: Role | null, viewport = DESKTOP): Promise<{ page: Page; close: () => Promise<void> }> {
  const mobile = viewport.width < 600;
  const context = await browser.newContext({
    baseURL,
    viewport,
    deviceScaleFactor: 1,
    locale: 'pl-PL',
    timezoneId: 'Europe/Warsaw',
    reducedMotion: 'reduce',
    isMobile: mobile,
    hasTouch: mobile,
    storageState: role ? sessions[role] : undefined
  });
  const page = await context.newPage();
  return { page, close: () => context.close() };
}

/** Czeka, aż strona się ustabilizuje: brak spinnerów/szkieletów, wczytane obrazy i fonty, wyłączone animacje. */
async function settle(page: Page): Promise<void> {
  await page.addStyleTag({
    content: '*, *::before, *::after { animation: none !important; transition: none !important; caret-color: transparent !important; }'
      // ogłaszacz CDK (aria-live) nie ma w aplikacji stylów „visually hidden” – bez ukrycia widać go pod treścią strony
      + ' .cdk-live-announcer-element { display: none !important; }'
  });
  await page.waitForLoadState('networkidle');
  await expect(page.locator('mat-spinner, mat-progress-spinner, mat-progress-bar, .skeleton, [aria-busy="true"]')).toHaveCount(0);
  await page.evaluate(() => document.fonts.ready);
  await expect.poll(() => page.evaluate(() =>
    Array.from(document.images).filter(image => !image.complete || image.naturalWidth === 0).length)).toBe(0);
  // zdjęcia i tła ładowane jako blob/CSS – krótka chwila na namalowanie
  await page.waitForTimeout(400);
}

interface ShotOptions {
  /** Pełna strona (domyślnie tak), przycięta do MAX_HEIGHT. */
  fullPage?: boolean;
  maxHeight?: number;
  /** Selektor elementów, na których dolnej krawędzi kończy się zrzut (przycięcie nie tnie kart/wierszy w pół). */
  snapTo?: string;
  /** Margines pod ostatnim dopasowanym elementem `snapTo` (px, domyślnie 28 – widać początek kolejnej karty). */
  snapPad?: number;
  /** Wykonaj zrzut elementu zamiast strony (z marginesem 24 px). */
  locator?: Locator;
  /** Fragment strony (współrzędne dokumentu) – gdy trzeba pokazać wycinek. */
  clip?: { x: number; y: number; width: number; height: number };
  /** Elementy do ukrycia (np. dane zmienne). */
  hide?: string[];
}

/** Zapisuje zrzut do docs/screenshots/<name>.png i optymalizuje go (ImageMagick, jeśli jest dostępny). */
async function shoot(page: Page, name: string, options: ShotOptions = {}): Promise<void> {
  const file = resolve(OUT_DIR, `${name}.png`);
  if (options.hide?.length) {
    await page.addStyleTag({ content: `${options.hide.join(', ')} { visibility: hidden !important; }` });
  }
  await page.mouse.move(0, 0);
  if (options.locator) {
    // boundingBox() jest względem okna – clip pełnej strony potrzebuje współrzędnych dokumentu
    const box = (await options.locator.boundingBox())!;
    const scroll = await page.evaluate(() => ({ x: window.scrollX, y: window.scrollY }));
    const margin = 24;
    const x = Math.max(0, box.x + scroll.x - margin);
    const y = Math.max(0, box.y + scroll.y - margin);
    await page.screenshot({
      path: file,
      fullPage: true,
      animations: 'disabled',
      clip: { x, y, width: box.width + margin + (box.x + scroll.x - x), height: box.height + margin + (box.y + scroll.y - y) }
    });
  } else if (options.clip) {
    await page.screenshot({ path: file, fullPage: true, clip: options.clip, animations: 'disabled' });
  } else if (options.fullPage === false) {
    await page.screenshot({ path: file, animations: 'disabled' });
  } else {
    const viewport = page.viewportSize()!;
    const height = await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight));
    const limit = options.maxHeight ?? MAX_HEIGHT;
    let clipHeight = Math.min(Math.max(height, viewport.height), limit);
    if (options.snapTo && height > limit) {
      const bottom = await page.evaluate(({ selector, max }) => {
        const bottoms = Array.from(document.querySelectorAll(selector))
          .map(element => element.getBoundingClientRect().bottom + window.scrollY)
          .filter(value => value <= max - 24);
        return bottoms.length ? Math.max(...bottoms) : 0;
      }, { selector: options.snapTo, max: limit });
      if (bottom > 0) clipHeight = Math.ceil(bottom + (options.snapPad ?? 28));
    }
    await page.screenshot({ path: file, fullPage: true, clip: { x: 0, y: 0, width: viewport.width, height: clipHeight }, animations: 'disabled' });
  }
  optimize(file);
  console.log(`  ${name}.png  ${(statSync(file).size / 1024).toFixed(0)} kB`);
}

/** Kwantyzacja palety (UI ma mało kolorów) – PNG zwykle < 300 kB; bez ImageMagick zostaje oryginał. */
function optimize(file: string): void {
  const tmp = `${file}.tmp.png`;
  try {
    execFileSync('convert', [file, '-colors', '192', '-dither', 'None', '-define', 'png:compression-level=9', `PNG8:${tmp}`], { stdio: 'ignore' });
    if (statSync(tmp).size < statSync(file).size) renameSync(tmp, file);
  } catch {
    // brak ImageMagick albo błąd konwersji – zostaje zrzut z Playwrighta
  } finally {
    rmSync(tmp, { force: true });
  }
}

async function jobIdByTitle(page: Page, title: string): Promise<number> {
  const response = await page.request.get('/api/jobs', { params: { searchTerm: title, pageSize: '50' } });
  const jobs = await response.json() as { id: number; title: string }[];
  const job = jobs.find(item => item.title === title);
  if (!job) throw new Error(`Brak oferty „${title}” – uruchom na stacku z danymi demo`);
  return job.id;
}

async function companyIdByName(page: Page, name: string): Promise<number> {
  const response = await page.request.get('/api/companies', { params: { searchTerm: name, pageSize: '50' } });
  const companies = await response.json() as { id: number; name: string }[];
  const company = companies.find(item => item.name === name);
  if (!company) throw new Error(`Brak firmy „${name}” – uruchom na stacku z danymi demo`);
  return company.id;
}

// ---------------------------------------------------------------------------------------------------------------------
// publiczne

test.describe('publiczne', () => {
  test('lista ofert z filtrami', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null);
    await page.goto('/jobs?workType=1');
    await expect(page.locator('article.job-card').first()).toBeVisible();
    await settle(page);
    await shoot(page, '01-job-list', { maxHeight: 1250, snapTo: 'article.job-card' });
    await close();
  });

  test('szczegóły oferty z tłem firmy', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null);
    await page.goto(`/jobs/${await jobIdByTitle(page, 'Frontend Developer (Angular)')}`);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('section.hero')).toHaveClass(/has-image/);
    await settle(page);
    await shoot(page, '02-job-detail');
    await close();
  });

  test('profil firmy z galerią i dialog galerii', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null);
    await page.goto(`/companies/${await companyIdByName(page, 'TechCorp Solutions')}`);
    const gallery = page.getByRole('list', { name: 'Zdjęcia firmy' });
    await expect(gallery.getByRole('listitem').first()).toBeVisible();
    await settle(page);
    await shoot(page, '03-company-profile');

    await page.setViewportSize({ width: 1440, height: 800 });
    await gallery.getByRole('button', { name: /Powiększ zdjęcie/ }).nth(1).click();
    const dialog = page.getByRole('dialog', { name: /Zdjęcia firmy/ });
    await expect(dialog.getByRole('img')).toBeVisible();
    await settle(page);
    await shoot(page, '04-gallery-dialog', { fullPage: false });
    await close();
  });

  test('logowanie i rejestracja', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null);
    await page.goto('/login');
    await expect(page.getByRole('button', { name: 'Zaloguj się' })).toBeVisible();
    await page.getByText('Konta demonstracyjne').click();
    await settle(page);
    await shoot(page, '05-login', { maxHeight: 640 });

    await page.goto('/register');
    await expect(page.getByRole('heading', { name: 'Załóż konto' })).toBeVisible();
    await settle(page);
    await shoot(page, '06-register', { maxHeight: 760 });
    await close();
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// kandydat

test.describe('kandydat', () => {
  test('sekcja „Twoja aplikacja”', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'candidate');
    await page.goto(`/jobs/${await jobIdByTitle(page, 'UX/UI Designer')}`);
    const section = page.locator('app-job-apply');
    await expect(section.getByRole('heading', { name: 'Twoja aplikacja' })).toBeVisible();
    await settle(page);
    await section.getByRole('checkbox').first().check();
    await section.getByRole('textbox').fill('Dzień dobry, chętnie dołączę do zespołu – od lat projektuję interfejsy dla aplikacji biznesowych.');
    await section.scrollIntoViewIfNeeded();
    await shoot(page, '07-job-apply', { locator: section });
    await close();
  });

  test('moje aplikacje z rozwiniętą historią', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'candidate');
    await page.goto('/my-applications');
    await expect(page.locator('article.application').first()).toBeVisible();
    await page.getByRole('button', { name: 'Szczegóły aplikacji: Senior .NET Developer' }).click();
    await expect(page.getByRole('heading', { name: 'Historia' })).toBeVisible();
    await settle(page);
    await shoot(page, '08-my-applications');
    await close();
  });

  test('zapisane oferty', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'candidate');
    await page.goto('/saved-jobs');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('article').first()).toBeVisible();
    await settle(page);
    await shoot(page, '09-saved-jobs', { maxHeight: 680 });
    await close();
  });

  test('moje konto', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'candidate');
    await page.goto('/account');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await settle(page);
    await shoot(page, '10-account', { maxHeight: 1290 });
    await close();
  });

  test('e-mail o zmianie statusu w Mailpicie', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null);
    const search = await page.request.get(`${MAILPIT_URL}/api/v1/search`, { params: { query: `to:${CANDIDATE.email}` } });
    const messages = (await search.json() as { messages: { ID: string; Subject: string }[] }).messages;
    const message = messages.find(item => /status/i.test(item.Subject));
    if (!message) throw new Error('Brak e-maila o zmianie statusu w Mailpit – uruchom na stacku z danymi demo');
    await page.setViewportSize({ width: 1100, height: 840 });
    await page.goto(`${MAILPIT_URL}/view/${message.ID}`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(800);
    await shoot(page, '14-email', { fullPage: false });
    await close();
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// pracodawca

test.describe('pracodawca', () => {
  test('panel – oferty', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'employer');
    await page.goto('/employer');
    await expect(page.getByRole('tab', { name: 'Oferty' })).toBeVisible();
    await expect(page.locator('table, .offer-row, article').first()).toBeVisible();
    await settle(page);
    await shoot(page, '11-employer-offers');
    await close();
  });

  test('panel – aplikacje', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'employer');
    await page.goto('/employer');
    await page.getByRole('tab', { name: 'Aplikacje' }).click();
    await expect(page.getByText('Jan Kowalski').first()).toBeVisible();
    await settle(page);
    await shoot(page, '12-employer-applications');
    await close();
  });

  test('panel – profil firmy (zdjęcia i tło)', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'employer');
    await page.goto('/employer');
    await page.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(page.getByRole('heading', { level: 2, name: 'Zdjęcia' })).toBeVisible();
    await expect(page.locator('app-company-photos li.tile.photo').first()).toBeVisible();
    await expect(page.locator('app-offer-background-settings .preview img')).toBeVisible();
    await settle(page);
    await shoot(page, '13-employer-profile', { maxHeight: 1700 });
    await close();
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// admin

test.describe('admin', () => {
  test('panel – statystyki', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'admin');
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Panel admina' })).toBeVisible();
    await settle(page);
    await shoot(page, '15-admin-stats', { maxHeight: 1640, snapTo: 'tbody tr', snapPad: 4 });
    await close();
  });

  test('panel – weryfikacja firm', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'admin');
    await page.goto('/admin');
    await page.getByRole('tab', { name: 'Firmy' }).click();
    await expect(page.locator('mat-tab-body.mat-mdc-tab-body-active').getByText('Nowa Firma Sp. z o.o.').first()).toBeVisible();
    await settle(page);
    await shoot(page, '16-admin-companies');
    await close();
  });

  test('panel – kategorie', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'admin');
    await page.goto('/admin');
    await page.getByRole('tab', { name: 'Kategorie' }).click();
    await expect(page.locator('mat-tab-body.mat-mdc-tab-body-active').getByText('Programowanie').first()).toBeVisible();
    await settle(page);
    await shoot(page, '17-admin-categories');
    await close();
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// mobile

test.describe('mobile', () => {
  test('lista ofert', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null, MOBILE);
    await page.goto('/jobs');
    await expect(page.locator('article.job-card').first()).toBeVisible();
    await settle(page);
    await shoot(page, 'm1-job-list', { maxHeight: 1500, snapTo: 'article.job-card' });
    await close();
  });

  test('szczegóły oferty', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null, MOBILE);
    await page.goto(`/jobs/${await jobIdByTitle(page, 'Frontend Developer (Angular)')}`);
    await expect(page.locator('section.hero')).toHaveClass(/has-image/);
    await settle(page);
    await shoot(page, 'm2-job-detail', { maxHeight: 1500 });
    await close();
  });

  test('moje aplikacje', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, 'candidate', MOBILE);
    await page.goto('/my-applications');
    await expect(page.locator('article.application').first()).toBeVisible();
    await page.getByRole('button', { name: 'Szczegóły aplikacji: Senior .NET Developer' }).click();
    await expect(page.getByRole('heading', { name: 'Historia' })).toBeVisible();
    await settle(page);
    await shoot(page, 'm3-my-applications', { maxHeight: 1500 });
    await close();
  });

  test('profil firmy', async ({ browser, baseURL }) => {
    const { page, close } = await open(browser, baseURL, null, MOBILE);
    await page.goto(`/companies/${await companyIdByName(page, 'TechCorp Solutions')}`);
    await expect(page.getByRole('list', { name: 'Zdjęcia firmy' }).getByRole('listitem').first()).toBeVisible();
    await settle(page);
    await shoot(page, 'm4-company-profile', { maxHeight: 1540 });
    await close();
  });
});
