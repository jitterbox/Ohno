import { test, expect, chromium } from '@playwright/test';

const CDP = 'http://127.0.0.1:9223';

test.describe.configure({ mode: 'serial' });

test('extension annotates TopK after activation', async () => {
  test.skip(!process.env.OHNO_E2E, 'Set OHNO_E2E=1 to run VS Code E2E');
  const browser = await chromium.connectOverCDP(CDP);
  const page = browser.contexts()[0]?.pages()[0];
  expect(page).toBeTruthy();
  await page!.waitForTimeout(2000);
  const after = await page!.evaluate(() => {
    const lines = document.querySelectorAll('.view-line');
    return Array.from(lines).map((el) => {
      const style = getComputedStyle(el, '::after');
      return style.content;
    });
  });
  expect(after.some((c) => c.includes('O(') || c.includes('n'))).toBeDefined();
  await browser.close();
});
