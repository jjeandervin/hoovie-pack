import { test, expect } from '@playwright/test';
const options = {
  traitScale: { min: 1, max: 5 }, exerciseMinutes: { min: 20, max: 120 }, lifeMaxYears: { min: 8, max: 20 },
  adultWeightKg: { min: 1, max: 113 }, adultHeightCm: { min: 15, max: 91 },
  breedGroups: [{ id: '11111111-1111-1111-1111-111111111111', name: 'Herding' }],
  coatTypes: ['curly', 'double'], coatLengths: ['short', 'medium'], coatColors: ['black', 'black and tan', 'red'],
  temperaments: ['friendly', 'intelligent', 'loyal'], recognizedBy: ['AKC'], originCountries: ['England', 'Scotland']
};
async function setup(page) {
  const requests = [];
  await page.route('**/api/dogipedia/breeds**', route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('filter-options')) return route.fulfill({ json: options });
    requests.push(url.searchParams);
    const totalItems = url.searchParams.has('hypoallergenicOnly') ? 0 : 2;
    return route.fulfill({ json: { items: totalItems ? [{ id: 'collie', name: 'Collie', descriptionExcerpt: 'A herding dog.', lifeMinYears: 12, lifeMaxYears: 14, image: null }] : [],
      page: Number(url.searchParams.get('page') || 1), pageSize: 24, totalItems, totalPages: totalItems ? 1 : 0, hasNextPage: false, hasPreviousPage: false } });
  });
  await page.goto('/dogipedia?search=collie');
  await expect(page.getByRole('heading', { name: 'Collie', exact: true })).toBeVisible();
  return requests;
}

test('expands, debounces keyboard sliders, combines search, collapses, removes and restores URL state', async ({ page }) => {
  const requests = await setup(page);
  await page.getByRole('button', { name: 'Filters', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Filters', exact: true })).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByText('Lifestyle & Personality', { exact: true })).toBeVisible();
  expect(requests.length).toBe(1);
  const slider = page.getByRole('slider', { name: 'Minimum Trainability' });
  await slider.focus();
  await slider.press('ArrowRight');
  await slider.press('ArrowRight');
  await slider.press('ArrowRight');
  await expect(page.getByRole('button', { name: 'Filters (1)', exact: true })).toBeVisible();
  expect(requests.length).toBe(1);
  await expect.poll(() => requests.length).toBe(2);
  await expect(page).toHaveURL(/search=collie.*trainabilityMin=4/);
  await expect(slider).toHaveAttribute('aria-valuetext', '4 out of 5');
  await page.getByRole('button', { name: 'Filters (1)', exact: true }).click();
  await expect(slider).toBeHidden();
  await expect(page.getByRole('button', { name: 'Remove Trainability 4+' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Remove Trainability 4+' })).toBeVisible();
  await page.getByRole('button', { name: 'Remove Trainability 4+' }).click();
  await expect(page).not.toHaveURL(/trainabilityMin/);
  await page.goBack();
  await expect(page.getByRole('button', { name: 'Filters (1)', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Clear all', exact: true }).click();
  await expect(page).not.toHaveURL(/trainabilityMin/);
  await expect(page.getByRole('searchbox', { name: 'Find your next favorite breed' })).toHaveValue('collie');
});

test('searchable multiple selection, range conversion and zero results', async ({ page }) => {
  const requests = await setup(page);
  await page.getByRole('button', { name: 'Filters', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Search Coat colors' }).fill('black');
  await page.getByRole('checkbox', { name: 'black', exact: true }).check();
  await page.getByRole('checkbox', { name: 'black and tan', exact: true }).check();
  await expect.poll(() => requests.at(-1).getAll('coatColors')).toEqual(['black', 'black and tan']);
  await page.getByRole('checkbox', { name: 'friendly', exact: true }).check();
  await page.getByRole('checkbox', { name: 'intelligent', exact: true }).check();
  await expect.poll(() => requests.at(-1).getAll('temperaments')).toEqual(['friendly', 'intelligent']);
  const minimumEnergy = page.getByRole('slider', { name: 'Minimum Energy' });
  const maximumEnergy = page.getByRole('slider', { name: 'Maximum Energy' });
  await minimumEnergy.evaluate(input => { input.value = '3'; input.dispatchEvent(new Event('input', { bubbles: true })); });
  await maximumEnergy.evaluate(input => { input.value = '2'; input.dispatchEvent(new Event('input', { bubbles: true })); });
  await expect(maximumEnergy).toHaveValue('3');
  await minimumEnergy.evaluate(input => { input.value = '4'; input.dispatchEvent(new Event('input', { bubbles: true })); });
  await expect(minimumEnergy).toHaveValue('3');
  const weight = page.getByRole('slider', { name: 'Maximum Adult weight' });
  await weight.evaluate(input => { input.value = '50'; input.dispatchEvent(new Event('input', { bubbles: true })); });
  await expect.poll(() => Number(requests.at(-1).get('adultWeightMaxKg'))).toBeCloseTo(22.679619, 5);
  await page.screenshot({ path: 'test-results/advanced-desktop.png', fullPage: true });
  await page.getByRole('checkbox', { name: 'Hypoallergenic only' }).check();
  await expect(page.getByRole('heading', { name: 'No breeds match all of those filters.' })).toBeVisible();
  await expect(page.getByText('0 breeds found', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Remove Hypoallergenic', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Collie', exact: true })).toBeVisible();
});

test('mobile filters remain within viewport and secondary sections are keyboard accessible', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await setup(page);
  await page.getByRole('button', { name: 'Filters', exact: true }).click();
  await expect(page.getByText('Size & Exercise', { exact: true })).toBeVisible();
  const more = page.locator('summary').filter({ hasText: 'More filters' });
  await more.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('searchbox', { name: 'Search Origin country' })).toBeVisible();
  await page.getByRole('searchbox', { name: 'Search Origin country' }).fill('Scot');
  await page.getByRole('checkbox', { name: 'Scotland' }).check();
  await expect(page).toHaveURL(/originCountries=scotland/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/advanced-mobile.png', fullPage: true });
});


for (const width of [1280, 375]) {
  test(`filter edits preserve scroll position at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 812 });
    await setup(page);
    await page.getByRole('button', { name: 'Filters', exact: true }).click();
    const slider = page.getByRole('slider', { name: 'Maximum Shedding' });
    await slider.scrollIntoViewIfNeeded();
    await slider.focus();
    const beforeSlider = (await slider.boundingBox()).y;
    expect(await page.evaluate(() => window.scrollY)).toBeGreaterThan(100);
    await slider.press('ArrowLeft');
    await expect(page).toHaveURL(/sheddingMax=4/);
    await expect(page.locator('#breed-results')).toHaveAttribute('aria-busy', 'false');
    // Allow the router's scheduled post-navigation scroll to run.
    await page.waitForTimeout(150);
    // New chips can change document height; the active control must stay put in the viewport.
    expect(Math.abs((await slider.boundingBox()).y - beforeSlider)).toBeLessThan(5);
    expect(await page.evaluate(() => window.scrollY)).toBeGreaterThan(100);
    await expect(slider).toBeFocused();

    const checkbox = page.getByRole('checkbox', { name: 'Hypoallergenic only' });
    await checkbox.scrollIntoViewIfNeeded();
    const beforeCheckbox = (await checkbox.boundingBox()).y;
    await checkbox.check();
    await expect(page).toHaveURL(/hypoallergenicOnly=true/);
    await expect(page.getByRole('heading', { name: 'No breeds match all of those filters.' })).toBeVisible();
    await page.waitForTimeout(150);
    expect(Math.abs((await checkbox.boundingBox()).y - beforeCheckbox)).toBeLessThan(5);
  });
}
