import { test, expect } from '@playwright/test';

async function editorApi(page, { unavailable = false, empty = false } = {}) {
  let dog = { id: 'pup', familyId: 'pack', name: 'Hermes', breed: 'Family mystery mix', canManage: true };
  const saves = [];
  const searches = [];
  await page.route('**/api/families/pack/members', route => route.fulfill({ json: [] }));
  await page.route('**/api/families/pack/dogs**', route => {
    if (['POST', 'PUT'].includes(route.request().method())) {
      const body = route.request().postDataJSON();
      saves.push(body);
      dog = { ...dog, ...body };
    }
    return route.fulfill({ json: dog });
  });
  await page.route('**/api/dogipedia/breeds?**', async route => {
    const url = new URL(route.request().url());
    searches.push(url);
    if (unavailable) return route.fulfill({ status: 503, json: { code: 'dogipedia_catalog_unavailable' } });
    const search = url.searchParams.get('search').toLowerCase();
    if (search === 'slow') await new Promise(resolve => setTimeout(resolve, 800));
    const names = empty ? [] : search === 'slow' ? ['Old result'] : search.includes('corgi') || search === 'little herder'
      ? ['Cardigan Welsh Corgi', 'Pembroke Welsh Corgi'] : [];
    await route.fulfill({ json: { items: names.map((name, i) => ({ id: `breed-${i}`, name })),
      page: 1, pageSize: 8, totalItems: names.length, totalPages: 1, hasPreviousPage: false, hasNextPage: false } }).catch(() => {});
  });
  return { searches, saves };
}

test('breed autocomplete debounces local searches and supports pointer and keyboard selection', async ({ page }, testInfo) => {
  const { searches, saves } = await editorApi(page);
  await page.goto('/dogs/new');
  const breed = page.getByRole('combobox', { name: 'Breed Optional' });
  await expect(breed).toBeVisible();
  expect(searches).toHaveLength(0);
  await breed.fill('cor');
  await page.waitForTimeout(100);
  expect(searches).toHaveLength(0);
  await breed.fill('corgi');
  await expect(page.getByRole('listbox').getByRole('option')).toHaveCount(2);
  expect(searches).toHaveLength(1);
  expect(searches[0].searchParams.get('pageSize')).toBe('8');
  await expect(breed).toHaveAttribute('aria-expanded', 'true');
  await page.getByRole('option', { name: 'Pembroke Welsh Corgi' }).click();
  await expect(breed).toHaveValue('Pembroke Welsh Corgi');
  await expect(breed).toHaveAttribute('aria-expanded', 'false');
  await breed.fill('little herder'); // Alternate-name matches use the server's canonical names.
  await expect(page.getByRole('listbox').getByRole('option')).toHaveCount(2);
  await breed.press('ArrowDown');
  await expect(page.getByRole('option', { name: 'Cardigan Welsh Corgi' })).toHaveAttribute('aria-selected', 'true');
  await page.locator('.form-card.stack-form').first().screenshot({ path: testInfo.outputPath('breed-suggestions.png') });
  await breed.press('Enter');
  await expect(breed).toHaveValue('Cardigan Welsh Corgi');
  expect(saves).toHaveLength(0);
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Hermes');
  await page.getByRole('button', { name: 'Add to the pack' }).click();
  await expect(page).toHaveURL(/\/dogs\/pup$/);
  expect(saves[0].breed).toBe('Cardigan Welsh Corgi');
  expect(saves[0]).not.toHaveProperty('breedId');
});

for (const unavailable of [false, true]) {
  test(`custom breed text saves with ${unavailable ? 'an unavailable catalog' : 'no matching suggestions'}`, async ({ page }) => {
    const { saves } = await editorApi(page, { unavailable, empty: true });
    await page.goto('/dogs/new');
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Scout');
    const breed = page.getByRole('combobox', { name: 'Breed Optional' });
    const response = page.waitForResponse('**/api/dogipedia/breeds?**');
    await breed.fill('Corgi / terrier mix');
    await response;
    await expect(page.getByRole('listbox')).toHaveCount(0);
    await expect(breed).toHaveValue('Corgi / terrier mix');
    await page.getByRole('button', { name: 'Add to the pack' }).click();
    await expect(page).toHaveURL(/\/dogs\/pup$/);
    expect(saves[0].breed).toBe('Corgi / terrier mix');
  });
}

test('typing cancels stale suggestions and Escape or blur preserves free text', async ({ page }) => {
  await editorApi(page);
  await page.goto('/dogs/new');
  const breed = page.getByRole('combobox', { name: 'Breed Optional' });
  const pending = page.waitForRequest('**/api/dogipedia/breeds?search=slow&**');
  await breed.fill('slow');
  await pending;
  await breed.fill('corgi');
  await expect(page.getByRole('option', { name: 'Pembroke Welsh Corgi' })).toBeVisible();
  await page.waitForTimeout(900);
  await expect(page.getByRole('option', { name: 'Old result' })).toHaveCount(0);
  await breed.press('ArrowDown');
  await breed.press('Escape');
  await expect(page.getByRole('listbox')).toHaveCount(0);
  await expect(breed).toHaveValue('corgi');
  await breed.press('ArrowDown');
  await expect(page.getByRole('listbox')).toBeVisible();
  await breed.press('Tab');
  await expect(page.getByRole('listbox')).toHaveCount(0);
  await expect(breed).toHaveValue('corgi');
  await breed.fill('');
  await expect(page.getByRole('listbox')).toHaveCount(0);
});

test('editing preserves an existing free-text breed and suggestions fit mobile', async ({ page }, testInfo) => {
  const { saves } = await editorApi(page);
  await page.setViewportSize({ width: 390, height: 900 });
  await page.goto('/dogs/pup/edit');
  const breed = page.getByRole('combobox', { name: 'Breed Optional' });
  await expect(breed).toHaveValue('Family mystery mix');
  await breed.fill('corgi');
  await expect(page.getByRole('listbox').getByRole('option')).toHaveCount(2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.locator('.form-card.stack-form').first().screenshot({ path: testInfo.outputPath('breed-suggestions-mobile.png') });
  await page.getByRole('option', { name: 'Pembroke Welsh Corgi' }).click();
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page).toHaveURL(/\/dogs\/pup$/);
  expect(saves[0].breed).toBe('Pembroke Welsh Corgi');
});
