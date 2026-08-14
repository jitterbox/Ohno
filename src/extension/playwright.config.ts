import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './test/e2e',
  timeout: 120_000,
  retries: 0,
  globalSetup: process.env.OHNO_E2E
    ? './test/e2e/global-setup.ts'
    : undefined,
  globalTeardown: process.env.OHNO_E2E
    ? './test/e2e/global-teardown.ts'
    : undefined,
  use: {
    trace: 'retain-on-failure',
  },
  reporter: [['list'], ['html', { open: 'never' }]],
});
