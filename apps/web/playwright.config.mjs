import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/browser', testMatch: '*.spec.mjs', fullyParallel: false,
  use: { baseURL: 'http://127.0.0.1:4317', headless: true,
    ...(process.env.PLAYWRIGHT_CHANNEL ? { channel: process.env.PLAYWRIGHT_CHANNEL } : {}) },
  webServer: { command: 'node tests/browser/server.mjs', url: 'http://127.0.0.1:4317', reuseExistingServer: !process.env.CI },
  reporter: 'list'
});
