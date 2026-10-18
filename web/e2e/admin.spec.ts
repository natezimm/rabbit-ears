import { test, expect, longTitle } from './fixtures';
import type { AdminCatalog, TvSnapshot } from '../src/app/core/models';

test('saves labels, numbering and playback modes across a server restart', async ({ page, box }) => {
  await page.goto(`${box.url}/admin`);
  const arthur = page.getByRole('group', { name: 'Arthur', exact: true });
  await arthur.getByRole('textbox', { name: 'Display name for Arthur' }).fill('PBS Kids');
  await arthur.getByRole('spinbutton', { name: 'Channel number' }).fill('8');
  await arthur.getByRole('combobox', { name: 'When tuning in' }).selectOption('Resume');
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toHaveText('Lineup saved. The TV and remotes have been updated.');
  await expect(page.getByRole('button', { name: 'Save lineup' })).toBeDisabled();

  await box.restart();
  await page.goto(`${box.url}/admin`);
  await expect(arthur.getByRole('textbox', { name: 'Display name for Arthur' })).toHaveValue('PBS Kids');
  await expect(arthur.getByRole('spinbutton', { name: 'Channel number' })).toHaveValue('8');
  await expect(arthur.getByRole('combobox', { name: 'When tuning in' })).toHaveValue('Resume');
});

test('a connected remote receives lineup changes without reloading', async ({ page, context, box }) => {
  const remote = await context.newPage();
  await remote.goto(`${box.url}/remote`);
  await expect(remote.getByRole('button', { name: /02 Arthur 2 ep/ })).toBeVisible();
  await page.goto(`${box.url}/admin`);
  const arthur = page.getByRole('group', { name: 'Arthur', exact: true });
  await arthur.getByRole('textbox', { name: 'Display name for Arthur' }).fill('PBS Kids');
  await arthur.getByRole('spinbutton', { name: 'Channel number' }).fill('8');
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(remote.getByRole('button', { name: /08 PBS Kids 2 ep/ })).toBeVisible();
  await expect(remote.getByRole('button', { name: /02 Arthur/ })).toHaveCount(0);
});

test('season and episode exclusions can remove and restore a channel', async ({ page, box }) => {
  await page.goto(`${box.url}/admin`);
  const arthur = page.getByRole('group', { name: 'Arthur', exact: true });
  await arthur.getByText('Choose episodes', { exact: false }).click();
  await arthur.getByRole('checkbox', { name: 'Season 1', exact: true }).uncheck();
  await expect(arthur.getByRole('checkbox', { name: /^First episode/ })).toBeDisabled();
  await arthur.getByRole('checkbox', { name: /^Second episode/ }).uncheck();
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  let state: TvSnapshot = await (await box.api.get('/api/state')).json();
  expect(state.channels.map(c => c.name)).toEqual(['Movies']);

  // Saving redraws the editor; open the restored details before checking controls.
  await page.reload();
  await arthur.getByText('Choose episodes', { exact: false }).click();
  await expect(arthur.getByRole('checkbox', { name: 'Season 1', exact: true })).not.toBeChecked();
  await arthur.getByRole('checkbox', { name: 'Season 1', exact: true }).check();
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  state = await (await box.api.get('/api/state')).json();
  expect(state.channels.find(c => c.name === 'Arthur')?.episodes).toBe(1);
});

test('hides and restores every channel without deleting media', async ({ page, box }) => {
  await page.goto(`${box.url}/admin`);
  for (const group of ['Arthur', 'Movies'])
    await page.getByRole('group', { name: group, exact: true }).getByRole('checkbox', { name: 'On the dial' }).uncheck();
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  const state: TvSnapshot = await (await box.api.get('/api/state')).json();
  expect(state.playing).toBeNull();
  expect(state.channels).toEqual([]);
  await page.reload();
  const arthur = page.getByRole('group', { name: 'Arthur', exact: true });
  await expect(arthur).toBeVisible();
  await arthur.getByRole('checkbox', { name: 'On the dial' }).check();
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  const restored: TvSnapshot = await (await box.api.get('/api/state')).json();
  expect(restored.channels.map(c => c.name)).toEqual(['Arthur']);
});

test('retains a stale tab draft until the user discards it', async ({ page, context, box }) => {
  const other = await context.newPage();
  await page.goto(`${box.url}/admin`);
  await other.goto(`${box.url}/admin`);
  await expect(other.getByRole('textbox', { name: 'Display name for Arthur' })).toHaveValue('Arthur');
  await page.getByRole('textbox', { name: 'Display name for Arthur' }).fill('Saved elsewhere');
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();

  await other.getByRole('textbox', { name: 'Display name for Arthur' }).fill('My unsaved draft');
  await other.getByRole('button', { name: 'Save lineup' }).click();
  await expect(other.getByRole('alert')).toContainText('changed in another tab');
  await expect(other.getByRole('textbox', { name: 'Display name for Arthur' })).toHaveValue('My unsaved draft');
  await expect(other.getByRole('button', { name: 'Save lineup' })).toBeEnabled();
  await other.getByRole('button', { name: 'Discard edits & reload' }).click();
  await expect(other.getByRole('textbox', { name: 'Display name for Arthur' })).toHaveValue('Saved elsewhere');
  await expect(other.getByRole('alert')).toHaveCount(0);
});

test('shows validation errors and lets the user correct them', async ({ page, box }) => {
  await page.goto(`${box.url}/admin`);
  for (const group of ['Arthur', 'Movies'])
    await page.getByRole('group', { name: group, exact: true }).getByRole('spinbutton', { name: 'Channel number' }).fill('8');
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('alert')).toContainText('must be unique');
  await expect(page.getByRole('group', { name: 'Arthur', exact: true }).getByRole('spinbutton')).toHaveValue('8');
  await page.getByRole('group', { name: 'Movies', exact: true }).getByRole('spinbutton').fill('9');
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('a failed request keeps the draft available for a successful retry', async ({ page, box }) => {
  await page.goto(`${box.url}/admin`);
  await page.getByRole('textbox', { name: 'Display name for Arthur' }).fill('Still my draft');
  await page.route('**/api/admin/channels', async route => {
    if (route.request().method() === 'PUT') await route.abort('failed');
    else await route.continue();
  });
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Display name for Arthur' })).toHaveValue('Still my draft');
  await page.unroute('**/api/admin/channels');
  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  const catalog: AdminCatalog = await (await box.api.get('/api/admin/channels')).json();
  expect(catalog.channels[0].settings.name).toBe('Still my draft');
});

test('rescans added folders after saving or discarding pending edits', async ({ page, box }) => {
  await page.goto(`${box.url}/admin`);
  await expect(page.getByText('Old Rips/Needs converting.mkv', { exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'Display name for Arthur' }).fill('Unsaved');
  await expect(page.getByRole('button', { name: 'Rescan folders' })).toBeDisabled();
  await page.getByRole('button', { name: 'Discard edits & reload' }).click();
  await expect(page.getByRole('textbox', { name: 'Display name for Arthur' })).toHaveValue('Arthur');
  await box.addMedia('New Show/An added episode.mp4');
  await page.getByRole('button', { name: 'Rescan folders' }).click();
  await expect(page.getByRole('group', { name: 'New Show', exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('Media folders rescanned.');
});

test('long names fit the editor and remote at the configured screen width', async ({ page, box }, testInfo) => {
  await page.goto(`${box.url}/admin`);
  await page.getByRole('textbox', { name: 'Display name for Movies' }).fill('A'.repeat(80));
  await page.getByRole('group', { name: 'Movies', exact: true }).getByText('Choose episodes', { exact: false }).click();
  await expect(page.getByRole('checkbox', { name: new RegExp(`^${longTitle}`) })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await testInfo.attach('admin-layout', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });

  await page.getByRole('button', { name: 'Save lineup' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  await page.goto(`${box.url}/remote`);
  await page.getByRole('button', { name: /03 A{80} 1 ep/ }).click();
  await expect(page.locator('.lcd')).toContainText(longTitle);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
});

test.describe('public demo', () => {
  test.use({ demoMode: true });

  test('shows the unavailable state and rejects management requests', async ({ page, box }) => {
    await page.goto(`${box.url}/admin`);
    await expect(page.getByRole('heading', { name: 'Managed on the home box' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save lineup' })).toHaveCount(0);
    for (const response of [
      await box.api.get('/api/admin/channels'),
      await box.api.put('/api/admin/channels', { headers: { 'X-RabbitEars-Admin': '1' }, data: { version: 'none', channels: [] } }),
      await box.api.post('/api/admin/rescan', { headers: { 'X-RabbitEars-Admin': '1' } }),
    ]) expect(response.status()).toBe(404);
    await page.goto(`${box.url}/remote`);
    await expect(page.getByRole('button', { name: /02 Arthur 2 ep/ })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Manage channels' })).toHaveCount(0);
  });
});
