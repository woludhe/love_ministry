import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 20 * 60 * 1000,
  expect: { timeout: 20_000 },
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.BASE_URL ?? 'https://localhost:53826',
    ignoreHTTPSErrors: true,
  },
});
