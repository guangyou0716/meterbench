import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end check of the dashboard against the real .NET API.
 *
 * Starts the API on :5080 (fresh SQLite file under e2e/.data) and `ng serve` on :4200,
 * which proxies /api to the API. Screenshots go to e2e/screenshots for the CI artifact.
 */
const apiPort = 5080;
const webPort = 4200;

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  outputDir: './e2e/test-results',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env['CI'],
  timeout: 120_000,
  reporter: [['list'], ['html', { outputFolder: 'e2e/report', open: 'never' }]],
  use: {
    baseURL: `http://127.0.0.1:${webPort}`,
    viewport: { width: 1280, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // A fresh database each run keeps the deterministic seed's alarm sequence reproducible.
      command: `node e2e/start-api.mjs ${apiPort}`,
      url: `http://127.0.0.1:${apiPort}/api/devices`,
      timeout: 180_000,
      reuseExistingServer: false,
      stdout: 'pipe',
    },
    {
      command: `npx ng serve --proxy-config proxy.conf.json --host 127.0.0.1 --port ${webPort}`,
      url: `http://127.0.0.1:${webPort}`,
      timeout: 180_000,
      reuseExistingServer: false,
      stdout: 'pipe',
    },
  ],
});
