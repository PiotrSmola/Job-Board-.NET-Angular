import { Locator, Page } from '@playwright/test';
import { expect, makePng, scanA11y, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

test.use({ storageState: authFile('employer') });

const png = (name: string, rgb: [number, number, number]) => ({ name, mimeType: 'image/png', buffer: makePng(640, 400, rgb) });
const snackBar = (page: Page) => page.locator('.mat-mdc-snack-bar-container');

async function openProfileTab(page: Page): Promise<void> {
  await page.goto('/employer');
  await page.getByRole('tab', { name: 'Profil firmy' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Zdjęcia' })).toBeVisible();
  await expect(page.locator('.tile.skeleton')).toHaveCount(0);
}

const photosSection = (page: Page): Locator => page.locator('app-company-photos');
const tiles = (page: Page): Locator => photosSection(page).locator('li.tile.photo');
const captions = async (page: Page): Promise<string[]> =>
  (await tiles(page).locator('img').evaluateAll(images => images.map(image => (image as HTMLImageElement).alt)));

async function setCaption(page: Page, current: string, caption: string): Promise<void> {
  await photosSection(page).getByRole('button', { name: `Edytuj opis zdjęcia: ${current}` }).click();
  const dialog = page.getByRole('dialog', { name: 'Opis zdjęcia' });
  await expect(dialog.getByLabel('Opis')).toBeFocused();
  await dialog.getByLabel('Opis').fill(caption);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
  await expect(photosSection(page).getByRole('button', { name: `Edytuj opis zdjęcia: ${caption}` })).toBeVisible();
}

test.describe('pracodawca – profil firmy: zdjęcia', () => {
  test('wgrywa 2 zdjęcia PNG i odrzuca nieobsługiwany plik', async ({ page }) => {
    await openProfileTab(page);
    const before = await tiles(page).count();
    expect(before, 'firma demo bez zdjęć na starcie').toBe(0);

    const input = photosSection(page).locator('input[type=file]');
    await input.setInputFiles({ name: 'notatki.txt', mimeType: 'text/plain', buffer: Buffer.from('to nie jest obraz') });
    await expect(photosSection(page).getByRole('alert')).toBeVisible();
    await expect(tiles(page)).toHaveCount(0);

    await input.setInputFiles([png('biuro-a.png', [200, 60, 60]), png('biuro-b.png', [60, 120, 200])]);
    await expect(tiles(page)).toHaveCount(2);
    await expect(snackBar(page)).toContainText('Dodano zdjęcia: 2');
    await expect(photosSection(page).getByText('2 z 20 zdjęć')).toBeVisible();
    expect(await captions(page)).toEqual(['Zdjęcie 1', 'Zdjęcie 2']);
    // skrajne przyciski przesuwania są wyłączone
    await expect(photosSection(page).getByRole('button', { name: 'Przesuń w lewo: Zdjęcie 1' })).toBeHidden();
    await expect(photosSection(page).getByRole('button', { name: 'Przesuń w prawo: Zdjęcie 2' })).toBeHidden();
  });

  test('zmienia podpisy zdjęć (dialog, Escape i trwałość po przeładowaniu)', async ({ page }) => {
    await openProfileTab(page);
    // Escape zamyka dialog bez zmian i zwraca fokus
    const edit = photosSection(page).getByRole('button', { name: 'Edytuj opis zdjęcia: Zdjęcie 1' });
    await edit.click();
    await expect(page.getByRole('dialog', { name: 'Opis zdjęcia' })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(edit).toBeFocused();

    await setCaption(page, 'Zdjęcie 1', 'Biuro A');
    await setCaption(page, 'Zdjęcie 2', 'Biuro B');
    await page.reload();
    await page.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(tiles(page)).toHaveCount(2);
    expect(await captions(page)).toEqual(['Biuro A', 'Biuro B']);
  });

  test('zmienia kolejność przyciskami (z klawiatury) i przeciąganiem', async ({ page }) => {
    await openProfileTab(page);
    expect(await captions(page)).toEqual(['Biuro A', 'Biuro B']);

    const right = photosSection(page).getByRole('button', { name: 'Przesuń w prawo: Biuro A' });
    await right.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => captions(page)).toEqual(['Biuro B', 'Biuro A']);
    // fokus zostaje na przycisku przesuniętego zdjęcia
    await expect(photosSection(page).getByRole('button', { name: 'Przesuń w lewo: Biuro A' })).toBeFocused();
    await page.keyboard.press('Space');
    await expect.poll(() => captions(page)).toEqual(['Biuro A', 'Biuro B']);

    // przeciąganie drugiego zdjęcia przed pierwsze
    const first = tiles(page).nth(0);
    const second = tiles(page).nth(1);
    const a = (await first.boundingBox())!;
    const b = (await second.boundingBox())!;
    await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2);
    await page.mouse.down();
    await page.mouse.move(b.x + b.width / 2 - 20, b.y + b.height / 2, { steps: 5 });
    await page.mouse.move(a.x + a.width / 4, a.y + a.height / 2, { steps: 15 });
    const saved = page.waitForResponse(response => response.url().endsWith('/photos/order') && response.request().method() === 'PUT');
    await page.mouse.up();
    await expect.poll(() => captions(page)).toEqual(['Biuro B', 'Biuro A']);
    expect((await saved).status()).toBe(200);

    // kolejność zapisana na serwerze
    await page.reload();
    await page.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(tiles(page)).toHaveCount(2);
    expect(await captions(page)).toEqual(['Biuro B', 'Biuro A']);
  });

  test('usuwa zdjęcie przez okno potwierdzenia (Anuluj nic nie zmienia)', async ({ page }) => {
    await openProfileTab(page);
    const remove = photosSection(page).getByRole('button', { name: 'Usuń zdjęcie: Biuro A' });
    await remove.click();
    let dialog = page.getByRole('dialog', { name: 'Usunąć zdjęcie?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(tiles(page)).toHaveCount(2);
    await expect(remove).toBeFocused();

    await remove.click();
    dialog = page.getByRole('dialog', { name: 'Usunąć zdjęcie?' });
    await dialog.getByRole('button', { name: 'Usuń' }).click();
    await expect(tiles(page)).toHaveCount(1);
    expect(await captions(page)).toEqual(['Biuro B']);
  });

  test('galeria publiczna: kafel „+N zdjęć”, dialog ←/→/Escape i powrót fokusu', async ({ page, browser }) => {
    await openProfileTab(page);
    await photosSection(page).locator('input[type=file]').setInputFiles([
      png('p2.png', [60, 180, 90]), png('p3.png', [220, 180, 60]), png('p4.png', [150, 90, 200]),
      png('p5.png', [40, 200, 200]), png('p6.png', [230, 120, 160])
    ]);
    await expect(tiles(page)).toHaveCount(6);

    const anon = await browser.newContext({ storageState: undefined });
    try {
      const pub = await anon.newPage();
      const companyLink = page.getByRole('link', { name: 'Zobacz profil publiczny' });
      await page.goto('/employer');
      const href = (await companyLink.getAttribute('href'))!;
      await pub.goto(href);
      const gallery = pub.getByRole('list', { name: 'Zdjęcia firmy' });
      await expect(gallery.getByRole('listitem')).toHaveCount(5);
      const more = gallery.getByRole('button', { name: '+2 zdjęcia, pokaż wszystkie (6)' });
      await expect(more).toBeVisible();
      await expect(more).toContainText('+2 zdjęcia');
      // zdjęcia faktycznie się załadowały (nie są uszkodzone)
      await expect.poll(() => gallery.locator('img').first().evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);

      const firstTile = gallery.getByRole('button', { name: /Powiększ zdjęcie/ }).first();
      await firstTile.click();
      const dialog = pub.getByRole('dialog', { name: /Zdjęcia firmy/ });
      await expect(dialog).toBeVisible();
      await expect(dialog.getByRole('status')).toContainText('Zdjęcie 1 z 6');
      await expect(dialog.getByRole('img')).toBeVisible();
      await scanA11y(pub, 'galeria zdjęć firmy (dialog)');

      await pub.keyboard.press('ArrowRight');
      await expect(dialog.getByRole('status')).toContainText('Zdjęcie 2 z 6');
      await pub.keyboard.press('ArrowLeft');
      await expect(dialog.getByRole('status')).toContainText('Zdjęcie 1 z 6');
      await pub.keyboard.press('ArrowLeft');
      await expect(dialog.getByRole('status')).toContainText('Zdjęcie 6 z 6');
      await dialog.getByRole('button', { name: 'Następne zdjęcie' }).click();
      await expect(dialog.getByRole('status')).toContainText('Zdjęcie 1 z 6');

      await pub.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
      await expect(firstTile).toBeFocused();

      // kafel „+N” otwiera pełną galerię od piątego zdjęcia
      await more.click();
      await expect(pub.getByRole('dialog').getByRole('status')).toContainText('Zdjęcie 5 z 6');
      await pub.getByRole('button', { name: 'Zamknij' }).click();
      await expect(more).toBeFocused();
      await scanA11y(pub, 'profil firmy z galerią');
    } finally {
      await anon.close();
    }
  });

  test('widok mobilny galerii i panelu zdjęć nie przewija się w poziomie', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await openProfileTab(page);
    await expect(tiles(page).first()).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
});

test.describe('pracodawca – profil firmy: tło ofert', () => {
  test('dodaje tło, ustawia przyciemnienie i punkt ostrości, zapisuje; oferta firmy pokazuje tło', async ({ page, browser }) => {
    const offer = readState().offer!;
    await openProfileTab(page);
    const section = page.locator('app-offer-background-settings');
    await expect(section.getByText('Brak obrazu')).toBeVisible();
    await expect(section.getByRole('button', { name: 'Zapisz tło' })).toBeDisabled();

    await section.locator('input[type=file]').setInputFiles(png('tlo.png', [40, 70, 120]));
    await expect(section.getByRole('button', { name: 'Zmień obraz' })).toBeVisible();
    await expect(section.locator('.preview img')).toBeVisible();
    await expect(section.getByRole('button', { name: 'Usuń tło' })).toBeVisible();

    // suwak 55-95 z podglądem na żywo
    const slider = section.getByRole('slider');
    await expect(slider).toHaveAttribute('min', '55');
    await expect(slider).toHaveAttribute('max', '95');
    await slider.fill('90');
    await expect(section.getByText('Przyciemnienie: 90%')).toBeVisible();
    await expect(slider).toHaveAttribute('aria-valuetext', '90 procent');
    await section.getByRole('combobox', { name: 'Punkt ostrości' }).click();
    await page.getByRole('option').nth(1).click();
    await expect(section.getByRole('button', { name: 'Zapisz tło' })).toBeEnabled();
    await section.getByRole('button', { name: 'Zapisz tło' }).click();
    await expect(section.getByRole('button', { name: 'Zapisz tło' })).toBeDisabled();

    await page.reload();
    await page.getByRole('tab', { name: 'Profil firmy' }).click();
    await expect(page.locator('app-offer-background-settings').getByText('Przyciemnienie: 90%')).toBeVisible();

    // oferta firmy pokazuje tło w nagłówku
    const anon = await browser.newContext({ storageState: undefined });
    try {
      const pub = await anon.newPage();
      await pub.goto(`/jobs/${offer.id}`);
      const hero = pub.locator('section.hero');
      await expect(hero).toHaveClass(/has-image/);
      await expect(hero).toHaveAttribute('style', /url\(/);
      await expect(hero).toHaveAttribute('style', /--hero-dim/);
      await scanA11y(pub, 'oferta z tłem firmy');
    } finally {
      await anon.close();
    }
  });

  test('usuwa tło (potwierdzenie) – nagłówek oferty wraca do jednolitego', async ({ page, browser }) => {
    const offer = readState().offer!;
    await openProfileTab(page);
    const section = page.locator('app-offer-background-settings');
    await section.getByRole('button', { name: 'Usuń tło' }).click();
    const dialog = page.getByRole('dialog', { name: 'Usunąć tło ofert?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Usuń tło' }).click();
    await expect(section.getByText('Brak obrazu')).toBeVisible();

    const anon = await browser.newContext({ storageState: undefined });
    try {
      const pub = await anon.newPage();
      await pub.goto(`/jobs/${offer.id}`);
      await expect(pub.locator('section.hero')).not.toHaveClass(/has-image/);
    } finally {
      await anon.close();
    }
  });

  test('sprzątanie: usuwa wgrane zdjęcia', async ({ page }) => {
    await openProfileTab(page);
    let remaining = await tiles(page).count();
    while (remaining > 0) {
      await photosSection(page).locator('li.tile.photo').first().getByRole('button', { name: /^Usuń zdjęcie/ }).click();
      await page.getByRole('dialog', { name: 'Usunąć zdjęcie?' }).getByRole('button', { name: 'Usuń' }).click();
      await expect(tiles(page)).toHaveCount(remaining - 1);
      remaining -= 1;
    }
    await expect(photosSection(page).getByText('0 z 20 zdjęć')).toBeVisible();
  });
});
