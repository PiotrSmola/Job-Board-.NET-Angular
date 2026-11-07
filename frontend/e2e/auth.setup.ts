import { mkdirSync, rmSync } from 'node:fs';
import { test as setup, expect } from '@playwright/test';
import { ADMIN, AUTH_DIR, EMPLOYER, authFile, writeState } from './fixtures/state';
import { extractAppLink, waitForMail } from './fixtures/mailpit';
import { uniqueId } from './fixtures/test';

/**
 * Przygotowanie sesji: nowy kandydat (rejestracja w UI + potwierdzenie e-maila linkiem z Mailpita),
 * pracodawca i admin (logowanie w UI). Sesje trafiają do storageState – limit logowań to 10/min na IP,
 * więc pozostałe testy już się nie logują.
 */
setup.describe.configure({ mode: 'serial' });

setup.beforeAll(() => {
  rmSync(AUTH_DIR, { recursive: true, force: true });
  mkdirSync(AUTH_DIR, { recursive: true });
});

setup('rejestracja i potwierdzenie e-maila kandydata', async ({ page, request }) => {
  const runId = uniqueId();
  const candidate = { email: `e2e.${runId}@example.com`, password: 'E2eTest123!', firstName: 'Ewa', lastName: `Testowa${runId}` };
  writeState({ candidate, runId, offer: undefined, appliedJob: undefined });

  await page.goto('/register');
  await expect(page.getByRole('heading', { name: 'Załóż konto' })).toBeVisible();
  await page.getByLabel('Imię').fill(candidate.firstName);
  await page.getByLabel('Nazwisko').fill(candidate.lastName);
  await page.getByLabel('E-mail').fill(candidate.email);
  await page.locator('input[formcontrolname="password"]').fill(candidate.password);
  await page.getByRole('button', { name: 'Załóż konto' }).click();

  // po rejestracji kandydat jest zalogowany, a baner prosi o potwierdzenie e-maila
  await expect(page.getByRole('button', { name: /Menu konta/ })).toBeVisible();
  await expect(page).toHaveURL(/\/jobs/);

  const mail = await waitForMail(request, candidate.email, 'Potwierdź adres e-mail');
  const link = extractAppLink(mail, '/confirm-email');
  await page.goto(link);
  await expect(page.getByText('Adres e-mail został potwierdzony')).toBeVisible();

  await page.context().storageState({ path: authFile('candidate') });
});

setup('logowanie pracodawcy', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('E-mail').fill(EMPLOYER.email);
  await page.locator('input[formcontrolname="password"]').fill(EMPLOYER.password);
  await page.getByRole('button', { name: 'Zaloguj się' }).click();
  await expect(page).toHaveURL(/\/employer/);
  await page.context().storageState({ path: authFile('employer') });
});

setup('logowanie admina', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('E-mail').fill(ADMIN.email);
  await page.locator('input[formcontrolname="password"]').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Zaloguj się' }).click();
  await expect(page.getByRole('button', { name: /Menu konta/ })).toBeVisible();
  await page.goto('/admin');
  await expect(page.getByRole('heading', { name: 'Panel admina' })).toBeVisible();
  await page.context().storageState({ path: authFile('admin') });
});
