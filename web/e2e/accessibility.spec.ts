import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';

import { expect, test } from './fixtures';

/** WCAG 2.2 AA plus axe's best practices, which is what the UI review measured against. */
async function audit(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'])
    .analyze();
  return results.violations.map(v => `${v.id}: ${v.help} (${v.nodes.map(n => n.target.join(' ')).join(', ')})`);
}

test.describe('at home', () => {
  test('the remote has no accessibility violations, connected or not', async ({ page, box }) => {
    await page.goto(`${box.url}/remote`);
    await expect(page.locator('.lcd-big')).toContainText('CH 02');
    expect(await audit(page)).toEqual([]);

    await page.route('**/hub/tv**', route => route.abort());
    await page.goto(`${box.url}/remote`);
    await expect(page.locator('.lcd-big')).toHaveText('NO LINK');
    expect(await audit(page)).toEqual([]);
  });

  test('the TV page has no accessibility violations', async ({ page, box }) => {
    await page.goto(`${box.url}/tv`);
    await expect(page.getByRole('img', { name: 'Television picture' })).toBeAttached();
    expect(await audit(page)).toEqual([]);
  });

  test('the lineup editor has no accessibility violations', async ({ page, box }) => {
    await page.goto(`${box.url}/admin`);
    await expect(page.getByRole('group', { name: 'Arthur', exact: true })).toBeVisible();
    expect(await audit(page)).toEqual([]);
  });
});

test.describe('public demo', () => {
  test.use({ demoMode: true });

  test('the demo page has no accessibility violations', async ({ page, box }) => {
    await page.goto(`${box.url}/demo`);
    await expect(page.getByRole('region', { name: "What's on" })).toBeVisible();
    expect(await audit(page)).toEqual([]);
  });
});
