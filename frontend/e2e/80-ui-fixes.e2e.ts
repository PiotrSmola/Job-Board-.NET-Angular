import { Locator, Page } from '@playwright/test';
import { expect, makePng, scanA11yStrict, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

/**
 * Weryfikacja poprawek odświeżonego UI po stronie pracodawcy, admina i anonima (po 70, przed sprzątaniem 99):
 * wersjonowane adresy zdjęć firmy, przerwany upload i błąd zmiany kolejności, kontrast nagłówka oferty na białym
 * tle, brakująca oferta/firma, daty po polsku i axe na stronach z menu konta. Zdjęcia i tło są po sobie sprzątane.
 */
const COMPANY_ID = 1;
const png = (name: string, rgb: [number, number, number]) => ({ name, mimeType: 'image/png', buffer: makePng(640, 400, rgb) });
const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

const photosSection = (page: Page): Locator => page.locator('app-company-photos');
const tiles = (page: Page): Locator => photosSection(page).locator('li.tile.photo');
const photoIds = async (page: Page): Promise<number[]> =>
  (await tiles(page).evaluateAll(items => items.map(item => Number(item.getAttribute('data-photo-id')))));

async function openProfileTab(page: Page): Promise<void> {
  await page.goto('/employer');
  await page.getByRole('tab', { name: 'Profil firmy' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Zdjęcia' })).toBeVisible();
  await expect(page.locator('.tile.skeleton')).toHaveCount(0);
}

async function removeAllPhotos(page: Page): Promise<void> {
  await openProfileTab(page);
  let remaining = await tiles(page).count();
  while (remaining > 0) {
    await tiles(page).first().getByRole('button', { name: /^Usuń zdjęcie/ }).click();
    await page.getByRole('dialog', { name: 'Usunąć zdjęcie?' }).getByRole('button', { name: 'Usuń' }).click();
    await expect(tiles(page)).toHaveCount(remaining - 1);
    remaining -= 1;
  }
}

/** Zdjęcia firmy według serwera (publiczny odczyt, ta sama ścieżka co profil publiczny). */
async function serverPhotos(page: Page): Promise<{ id: number; url: string }[]> {
  const response = await page.request.get(`/api/companies/${COMPANY_ID}/photos`);
  expect(response.ok()).toBe(true);
  return await response.json() as { id: number; url: string }[];
}

/** Liczy kontrast WCAG dwóch kolorów sRGB (0–255). */
const luminance = ([r, g, b]: number[]) => {
  const lin = (v: number) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
};
const contrast = (a: number[], b: number[]) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

interface Measurement { name: string; color: number[]; worstContrast: number; lightestBackground: number[] }

/**
 * Mierzy kontrast tekstu elementu względem efektywnego tła: kolor tekstu z computed style, tło ze zrzutu ekranu
 * obszaru elementu zrobionego po ukryciu całego tekstu w kontenerze (każdy piksel tła, najgorszy kontrast).
 */
async function measure(page: Page, container: string, targets: { name: string; selector: string }[]): Promise<Measurement[]> {
  const colors: Record<string, number[]> = {};
  for (const target of targets) {
    colors[target.name] = await page.locator(target.selector).first().evaluate(element => {
      const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true })!;
      context.fillStyle = '#000';
      context.fillStyle = getComputedStyle(element).color;
      context.fillRect(0, 0, 1, 1);
      return Array.from(context.getImageData(0, 0, 1, 1).data.slice(0, 3));
    });
  }
  const hide = await page.addStyleTag({
    content: `${container} * { color: transparent !important; text-shadow: none !important; } ${container} mat-icon, ${container} svg { visibility: hidden !important; }`
  });
  try {
    const results: Measurement[] = [];
    for (const target of targets) {
      const locator = page.locator(target.selector).first();
      await locator.scrollIntoViewIfNeeded();
      const box = (await locator.boundingBox())!;
      const shot = await page.screenshot({ clip: { x: box.x, y: box.y, width: Math.max(1, box.width), height: Math.max(1, box.height) } });
      const pixels = await page.evaluate(async (base64: string) => {
        const image = new Image();
        image.src = `data:image/png;base64,${base64}`;
        await image.decode();
        const canvas = document.createElement('canvas');
        canvas.width = image.width;
        canvas.height = image.height;
        const context = canvas.getContext('2d', { willReadFrequently: true })!;
        context.drawImage(image, 0, 0);
        return Array.from(context.getImageData(0, 0, image.width, image.height).data);
      }, shot.toString('base64'));
      let worst = Infinity;
      let lightest = [0, 0, 0];
      for (let i = 0; i < pixels.length; i += 4) {
        const bg = [pixels[i], pixels[i + 1], pixels[i + 2]];
        worst = Math.min(worst, contrast(colors[target.name], bg));
        if (luminance(bg) > luminance(lightest)) lightest = bg;
      }
      results.push({ name: target.name, color: colors[target.name], worstContrast: Number(worst.toFixed(2)), lightestBackground: lightest });
    }
    return results;
  } finally {
    await hide.evaluate(style => style.remove());
  }
}

test.describe('pracodawca – poprawki UI', () => {
  test.use({ storageState: authFile('employer') });
  test.describe.configure({ mode: 'serial' });

  test('zdjęcia firmy: adresy z ?v=, galeria > 5 zdjęć (kafel „+N”) i usunięcie znika z galerii publicznej', async ({ page, browser }) => {
    await openProfileTab(page);
    expect(await tiles(page).count(), 'start bez zdjęć').toBe(0);
    await photosSection(page).locator('input[type=file]').setInputFiles([
      png('f1.png', [200, 60, 60]), png('f2.png', [60, 120, 200]), png('f3.png', [60, 180, 90]),
      png('f4.png', [220, 180, 60]), png('f5.png', [150, 90, 200]), png('f6.png', [40, 200, 200])
    ]);
    await expect(tiles(page)).toHaveCount(6);

    const before = await serverPhotos(page);
    expect(before).toHaveLength(6);
    for (const photo of before) expect(photo.url, 'adres zdjęcia z wersją').toMatch(/\?v=[^&]+$/);

    const anon = await browser.newContext({ storageState: undefined });
    try {
      const pub = await anon.newPage();
      await pub.goto(`/companies/${COMPANY_ID}`);
      const gallery = pub.getByRole('list', { name: 'Zdjęcia firmy' });
      await expect(gallery.getByRole('listitem')).toHaveCount(5);
      const srcs = await gallery.locator('img').evaluateAll(images => images.map(image => (image as HTMLImageElement).getAttribute('src')!));
      expect(srcs).toHaveLength(5);
      for (const src of srcs) expect(src).toContain('?v=');
      await expect(gallery.getByRole('button', { name: /^\+2 zdjęcia/ })).toBeVisible();
      await expect.poll(() => gallery.locator('img').first().evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);

      // axe z kaflem „+N”: bez label-content-name-mismatch i bez serious/critical
      await pub.waitForLoadState('networkidle');
      const violations = await scanA11yStrict(pub, 'profil firmy z galerią > 5 zdjęć');
      expect(violations.filter(v => v.id === 'label-content-name-mismatch')).toEqual([]);

      // usunięcie pierwszego zdjęcia przez pracodawcę
      const removed = before[0];
      await tiles(page).first().getByRole('button', { name: /^Usuń zdjęcie/ }).click();
      await page.getByRole('dialog', { name: 'Usunąć zdjęcie?' }).getByRole('button', { name: 'Usuń' }).click();
      await expect(tiles(page)).toHaveCount(5);

      await pub.reload();
      await expect(gallery.getByRole('listitem')).toHaveCount(5);
      const after = await gallery.locator('img').evaluateAll(images => images.map(image => (image as HTMLImageElement).getAttribute('src')!));
      expect(after, 'usunięte zdjęcie nie jest w galerii').not.toContain(removed.url);
      expect(after.every(src => src.includes('?v='))).toBe(true);
      await expect(gallery.getByRole('button', { name: /^\+\d+ zdjęcia/ })).toHaveCount(0);
      // sam plik usuniętego zdjęcia też zniknął z serwera
      expect((await pub.request.get(removed.url)).status()).toBe(404);
    } finally {
      await anon.close();
    }
  });

  test('upload: przerwanie po zapisie na serwerze i przed jego odebraniem – lista zgodna z serwerem', async ({ page, monitor }) => {
    monitor.allow(/Failed to load resource/, /Error uploading photos/);
    await openProfileTab(page);
    const start = await tiles(page).count();
    expect(start).toBe(5);

    // A: serwer zapisuje zdjęcie, ale odpowiedź dociera po 5 s – użytkownik przerywa w tym czasie
    await page.route('**/api/employer/company/photos', async route => {
      if (route.request().method() !== 'POST') return route.fallback();
      const response = await route.fetch();
      await sleep(5000);
      await route.fulfill({ response }).catch(() => undefined);
    });
    await photosSection(page).locator('input[type=file]').setInputFiles(png('przerwane-a.png', [10, 10, 10]));
    const cancel = photosSection(page).getByRole('button', { name: 'Przerwij' });
    await expect(cancel).toBeVisible();
    await expect.poll(async () => (await serverPhotos(page)).length, { timeout: 10_000 }).toBe(start + 1);
    const refreshed = page.waitForRequest(request => request.method() === 'GET' && request.url().endsWith(`/api/companies/${COMPANY_ID}/photos`));
    await cancel.click();
    await refreshed;
    await expect(cancel).toHaveCount(0);
    await expect(tiles(page)).toHaveCount(start + 1);
    expect(await photoIds(page)).toEqual((await serverPhotos(page)).map(photo => photo.id));
    await page.unroute('**/api/employer/company/photos');

    // B: przerwanie, zanim żądanie opuści przeglądarkę – serwer nic nie dostaje (albo dostaje i lista to pokazuje)
    await page.route('**/api/employer/company/photos', async route => {
      if (route.request().method() !== 'POST') return route.fallback();
      await sleep(3000);
      await route.continue().catch(() => undefined);
    });
    await photosSection(page).locator('input[type=file]').setInputFiles(png('przerwane-b.png', [90, 90, 90]));
    await expect(cancel).toBeVisible();
    await cancel.click();
    await expect(cancel).toHaveCount(0);
    await page.waitForTimeout(5000);
    const server = await serverPhotos(page);
    await page.reload();
    await page.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(tiles(page)).toHaveCount(server.length);
    expect(await photoIds(page)).toEqual(server.map(photo => photo.id));
    test.info().annotations.push({ type: 'wariant B', description: `serwer po przerwaniu: ${server.length} zdjęć (start ${start + 1})` });
    await page.unroute('**/api/employer/company/photos');
  });

  test('zmiana kolejności: wymuszony 400 przywraca i przeładowuje listę z serwera', async ({ page, monitor }) => {
    monitor.allow(/Failed to load resource/, /Error reordering photos/, /PUT .*\/photos\/order 400/);
    await openProfileTab(page);
    const ids = await photoIds(page);
    expect(ids.length).toBeGreaterThanOrEqual(3);

    // serwer zmienia kolejność „gdzie indziej” (inna karta) – panel o tym nie wie
    const token = await page.evaluate(() => localStorage.getItem('jobboard_token'));
    const serverOrder = [...ids].reverse();
    const put = await page.request.put('/api/employer/company/photos/order', {
      headers: { Authorization: `Bearer ${token}` },
      data: { photoIds: serverOrder }
    });
    expect(put.status()).toBe(200);
    expect(await photoIds(page), 'panel nadal pokazuje starą kolejność').toEqual(ids);

    await page.route('**/api/employer/company/photos/order', route =>
      route.request().method() === 'PUT'
        ? route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ message: 'Invalid photo order' }) })
        : route.fallback());
    const reloaded = page.waitForRequest(request => request.method() === 'GET' && request.url().endsWith(`/api/companies/${COMPANY_ID}/photos`));
    await photosSection(page).getByRole('button', { name: 'Przesuń w prawo: Zdjęcie 1' }).click();
    await reloaded;
    // lista pokazuje stan serwera (odwrócony), nie lokalne przesunięcie ani starą kolejność
    await expect.poll(() => photoIds(page)).toEqual(serverOrder);
    await expect(photosSection(page).getByRole('alert')).toBeVisible();
    expect((await serverPhotos(page)).map(photo => photo.id)).toEqual(serverOrder);
    await page.unroute('**/api/employer/company/photos/order');
  });

  test('sprzątanie zdjęć po testach poprawek', async ({ page }) => {
    await removeAllPhotos(page);
    await expect(photosSection(page).getByText('0 z 20 zdjęć')).toBeVisible();
    expect(await serverPhotos(page)).toEqual([]);
  });

  test('kontrast nagłówka oferty na białym tle z przyciemnieniem 55 (strona oferty i podgląd w panelu)', async ({ page, browser }) => {
    const offer = readState().offer!;
    await openProfileTab(page);
    const section = page.locator('app-offer-background-settings');
    try {
      await section.locator('input[type=file]').setInputFiles({ name: 'biale.png', mimeType: 'image/png', buffer: makePng(1600, 600, [255, 255, 255]) });
      await expect(section.getByRole('button', { name: 'Zmień obraz' })).toBeVisible();
      await expect(section.locator('.preview img')).toBeVisible();
      await section.getByRole('slider').fill('55');
      await expect(section.getByText('Przyciemnienie: 55%')).toBeVisible();

      // podgląd na żywo w panelu
      await expect.poll(() => section.locator('.preview img').evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
      const previewMask = await section.locator('.preview .mask').evaluate(element => (element as HTMLElement).style.background);
      const previewContrast = await measure(page, 'app-offer-background-settings .preview', [
        { name: 'podgląd: tytuł', selector: 'app-offer-background-settings .preview-title' },
        { name: 'podgląd: meta', selector: 'app-offer-background-settings .preview-meta' }
      ]);

      // przyciemnienie 55 mogło już zostać zapisane przy wcześniejszym uruchomieniu – wtedy nie ma czego zapisywać
      const save = section.getByRole('button', { name: 'Zapisz tło' });
      if (await save.isEnabled()) await save.click();
      await expect(save).toBeDisabled();

      const anon = await browser.newContext({ storageState: undefined, viewport: { width: 1280, height: 900 } });
      try {
        const pub = await anon.newPage();
        await pub.goto(`/jobs/${offer.id}`);
        const hero = pub.locator('section.hero');
        await expect(hero).toHaveClass(/has-image/);
        await expect(hero.locator('.hero-mask')).toHaveAttribute('style', /linear-gradient/);
        await pub.waitForLoadState('networkidle');
        await pub.waitForTimeout(500);
        const heroMask = await hero.locator('.hero-mask').evaluate(element => (element as HTMLElement).style.backgroundImage);
        // ta sama maska w podglądzie i w nagłówku (wspólna funkcja offerHeroMask)
        expect(previewMask).toBe(heroMask);
        expect(heroMask).toContain('82%');

        const results = await measure(pub, 'section.hero', [
          { name: 'tytuł (h1)', selector: 'section.hero h1' },
          { name: 'tekst drugorzędny (meta)', selector: 'section.hero .hero-meta > span' },
          { name: 'link „Wróć do wyników”', selector: 'section.hero a.back' },
          { name: 'link firmy', selector: 'section.hero a.company-link' }
        ]);
        const all = [...previewContrast, ...results];
        test.info().annotations.push({ type: 'kontrast', description: JSON.stringify(all) });
        console.log('KONTRAST', JSON.stringify(all));
        for (const m of all) {
          // tło faktycznie jest rozjaśnione białym obrazem (strona ma #121417), więc pomiar jest wiarygodny
          expect(Math.max(...m.lightestBackground), `${m.name}: obraz tła widoczny pod maską`).toBeGreaterThan(25);
          expect(m.worstContrast, `${m.name}: kontrast ≥ 4,5:1`).toBeGreaterThanOrEqual(4.5);
        }
        await pub.screenshot({ path: 'e2e/.artifacts/hero-white-dim55.png', clip: { x: 0, y: 0, width: 1280, height: 420 } });
        await scanA11yStrict(pub, 'oferta z białym tłem i przyciemnieniem 55');
      } finally {
        await anon.close();
      }
    } finally {
      await page.goto('/employer');
      await page.getByRole('tab', { name: 'Profil firmy' }).click();
      const settings = page.locator('app-offer-background-settings');
      // poczekaj na wczytanie ustawień (przycisk obrazu), dopiero potem sprawdź, czy tło istnieje
      await expect(settings.getByRole('button', { name: /^(Dodaj|Zmień) obraz$/ })).toBeVisible();
      const remove = settings.getByRole('button', { name: 'Usuń tło' });
      if (await remove.count()) {
        await remove.click();
        await page.getByRole('dialog', { name: 'Usunąć tło ofert?' }).getByRole('button', { name: 'Usuń tło' }).click();
      }
      await expect(settings.getByText('Brak obrazu')).toBeVisible();
      expect((await (await page.request.get(`/api/companies/${COMPANY_ID}`)).json()).offerBackground ?? null).toBeNull();
    }
  });

  test('axe (WCAG 2.2 AA + label-content-name-mismatch) na panelu pracodawcy z menu konta', async ({ page }) => {
    await page.goto('/employer');
    for (const tab of ['Oferty', 'Aplikacje', 'Profil firmy', 'Statystyki']) {
      await page.getByRole('tab', { name: tab }).click();
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(400);
      const violations = await scanA11yStrict(page, `pracodawca: ${tab}`);
      expect(violations.filter(v => v.id === 'label-content-name-mismatch')).toEqual([]);
    }
  });

  test('daty: karta firmy i statystyki bez błędów, daty w formacie polskim', async ({ page }) => {
    await page.goto('/employer');
    await page.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(page.getByRole('heading', { level: 2, name: 'Dane firmy' })).toBeVisible();
    const reviewed = page.getByText(/^Weryfikacja: /);
    if (await reviewed.count()) await expect(reviewed).toHaveText(/^Weryfikacja: \d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$/);
    await page.getByRole('tab', { name: 'Statystyki' }).click();
    await expect(page.getByRole('region', { name: 'Statystyki' })).toBeVisible();
    await page.waitForLoadState('networkidle');
    const text = await page.getByRole('region', { name: 'Statystyki' }).innerText();
    expect(text).not.toMatch(/\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.? \d{1,2}\b/);
  });
});

test.describe('anonim – brak oferty i firmy', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('/jobs/999999 i /companies/999999 pokazują komunikat w UI, bez console.error z aplikacji', async ({ page, monitor }) => {
    // przeglądarka sama zgłasza nieudane żądanie („Failed to load resource”) – to nie jest console.error aplikacji
    monitor.allow(/Failed to load resource/, /http: GET .*\/api\/(jobs|companies)\/999999 (404|400)/);
    const appErrors: string[] = [];
    page.on('console', message => {
      if (message.type() === 'error' && !/Failed to load resource|Cross-Origin-Opener-Policy/.test(message.text())) appErrors.push(message.text());
    });

    await page.goto('/jobs/999999');
    await expect(page.getByRole('heading', { level: 1, name: 'Oferta niedostępna' })).toBeVisible();
    await expect(page.getByRole('alert')).toContainText(/Oferta nie została znaleziona|Nie udało się/);
    await expect(page).toHaveTitle(/Oferta niedostępna/);

    await page.goto('/companies/999999');
    await expect(page.getByRole('heading', { level: 1, name: 'Nie znaleziono firmy' })).toBeVisible();
    await expect(page).toHaveTitle(/Firma niedostępna/);
    await page.waitForTimeout(500);
    expect(appErrors, 'console.error z aplikacji').toEqual([]);
  });
});

test.describe('admin – poprawki UI', () => {
  test.use({ storageState: authFile('admin') });

  test('daty w tabelach admina po polsku i axe z menu konta', async ({ page }) => {
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Panel admina' })).toBeVisible();
    const dateCell = /^\d{2}\.\d{2}\.\d{4}( \d{2}:\d{2})?$/;
    const found: Record<string, number> = {};
    for (const tab of ['Oferty', 'Firmy', 'Aplikacje']) {
      await page.getByRole('tab', { name: tab }).click();
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(500);
      const cells = (await page.locator('td').allInnerTexts()).map(text => text.replace(/^do /, '').trim());
      const dates = cells.filter(text => dateCell.test(text));
      found[tab] = dates.length;
      expect(dates.length, `daty w zakładce ${tab}`).toBeGreaterThan(0);
      expect(cells.filter(text => /\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.? \d{1,2}\b/.test(text))).toEqual([]);
      const violations = await scanA11yStrict(page, `admin: ${tab}`);
      expect(violations.filter(v => v.id === 'label-content-name-mismatch')).toEqual([]);
    }
    test.info().annotations.push({ type: 'daty', description: JSON.stringify(found) });
  });
});
