import { defineConfig, devices } from '@playwright/test';

/**
 * Zrzuty ekranu do README (docs/screenshots). Osobna konfiguracja z projektem „screenshots” – domyślne `npm run e2e`
 * jej nie uruchamia. Wymaga działającego stacku z danymi demo (adres: E2E_BASE_URL, domyślnie http://frontend;
 * Mailpit: E2E_MAILPIT_URL, domyślnie http://mailpit:8025). Uruchomienie: `npm run screenshots`.
 */
export default defineConfig({
  testDir: './e2e/screenshots',
  testMatch: /readme-screenshots\.ts/,
  outputDir: './e2e/test-results',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://frontend',
    locale: 'pl-PL',
    timezoneId: 'Europe/Warsaw',
    actionTimeout: 15_000,
    navigationTimeout: 30_000
  },
  projects: [
    { name: 'screenshots', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 } }
  ]
});
