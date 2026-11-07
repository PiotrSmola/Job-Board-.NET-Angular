import { expect, scanA11y, test } from './fixtures/test';
import { authFile, readState } from './fixtures/state';

test.use({ storageState: authFile('employer') });

test.describe('pracodawca – aplikacje kandydata', () => {
  test('widzi aplikację kandydata, pobiera załącznik i zmienia status z komentarzem', async ({ page }) => {
    const { candidate, appliedJob } = readState();
    expect(appliedJob, 'aplikacja z 30-candidate').toBeTruthy();
    const fullName = `${candidate!.firstName} ${candidate!.lastName}`;
    // imię mogło zostać zmienione w teście profilu
    const candidateName = new RegExp(`${candidate!.lastName}`);

    await page.goto('/employer');
    await page.getByRole('tab', { name: 'Aplikacje' }).click();
    await page.getByRole('textbox', { name: 'Szukaj' }).fill(candidate!.email);
    await expect(page.getByRole('status').filter({ hasText: /Znaleziono: 1 aplikacji/ })).toBeVisible();

    const row = page.getByRole('row').filter({ hasText: candidate!.email });
    await expect(row).toContainText(appliedJob!.title);
    await expect(row.getByText('Złożona')).toBeVisible();
    await expect(row).toContainText('Wiadomość kandydata z testu E2E');
    await expect(row).toContainText(candidateName);
    await expect(row.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ })).toBeVisible();
    await expect(row.getByRole('button', { name: /Pobierz .*: list-motywacyjny\.pdf/ })).toBeVisible();
    // pracodawca nie usuwa aplikacji (tylko admin)
    await expect(row.getByRole('button', { name: /Usuń aplikację/ })).toHaveCount(0);

    const [file] = await Promise.all([
      page.waitForEvent('download'),
      row.getByRole('button', { name: /Pobierz .*: cv-e2e\.pdf/ }).click()
    ]);
    expect(file.suggestedFilename()).toBe('cv-e2e.pdf');

    const trigger = row.getByRole('button', { name: /Zmień status aplikacji/ });
    await trigger.click();
    const dialog = page.getByRole('dialog', { name: 'Zmień status aplikacji' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText(appliedJob!.title)).toBeVisible();
    // „Zapisz” aktywne dopiero po zmianie statusu lub dodaniu komentarza
    await dialog.getByRole('combobox', { name: 'Nowy status' }).click();
    await page.getByRole('option', { name: 'Rozmowa' }).click();
    await dialog.getByLabel('Komentarz (opcjonalnie)').fill('Zapraszamy na rozmowę w czwartek o 10:00');
    await scanA11y(page, 'dialog zmiany statusu aplikacji');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row').filter({ hasText: candidate!.email }).getByText('Rozmowa')).toBeVisible();
    expect(fullName).toBeTruthy();

    // historia pokazuje komentarz rekrutera
    await page.getByRole('row').filter({ hasText: candidate!.email }).getByRole('button', { name: /Historia aplikacji/ }).click();
    const history = page.getByRole('dialog', { name: 'Historia aplikacji' });
    await expect(history.getByText('Zapraszamy na rozmowę w czwartek o 10:00')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(history).toBeHidden();
    // fokus wraca na przycisk, który otworzył okno
    await expect(page.getByRole('row').filter({ hasText: candidate!.email }).getByRole('button', { name: /Historia aplikacji/ })).toBeFocused();
  });

  test('filtr statusu w tabeli aplikacji', async ({ page }) => {
    await page.goto('/employer');
    await page.getByRole('tab', { name: 'Aplikacje' }).click();
    await page.getByRole('combobox', { name: 'Status' }).click();
    await page.getByRole('option', { name: 'Rozmowa' }).click();
    await expect(page.getByRole('button', { name: 'Wyczyść filtry' })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: 'Rozmowa' }).first()).toBeVisible();
    await page.getByRole('button', { name: 'Wyczyść filtry' }).click();
    await expect(page.getByRole('button', { name: 'Wyczyść filtry' })).toHaveCount(0);
  });
});
