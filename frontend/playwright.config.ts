import { defineConfig, devices } from '@playwright/test';

/**
 * Testy E2E (klikane) działają na uruchomionym stacku aplikacji (nginx + API pod /api).
 * Adres: E2E_BASE_URL (domyślnie http://frontend – nazwa usługi widoczna z kontenera deweloperskiego).
 * Pliki 10-… 50-… uruchamiają się po kolei (jeden worker): kolejne scenariusze korzystają z danych
 * utworzonych wcześniej (konto kandydata, jego aplikacja), a stan wymieniają przez e2e/.state/state.json.
 */
export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e/test-results',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['json', { outputFile: 'e2e/.artifacts/report.json' }]],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://frontend',
    locale: 'pl-PL',
    timezoneId: 'Europe/Warsaw',
    viewport: { width: 1280, height: 900 },
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    // zrzuty ekranu i ślady tylko dla nieudanych testów
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure'
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/, use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } } },
    {
      name: 'chromium',
      testMatch: /\d+-.*\.e2e\.ts/,
      dependencies: ['setup'],
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } }
    }
  ]
});
