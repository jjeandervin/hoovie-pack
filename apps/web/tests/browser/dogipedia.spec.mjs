import { test, expect } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  page.on('pageerror', error => console.error(error));
  page.on('console', message => { if (message.type() === 'error') console.error(message.text()); });
});

const cards = [
  { id: 'corgi', name: 'Pembroke Welsh Corgi', descriptionExcerpt: 'A bright, loyal companion with a big personality.', groupName: 'Herding', lifeMinYears: 12, lifeMaxYears: 13, image: null },
  { id: 'akita', name: 'Akita', descriptionExcerpt: 'A devoted companion.', groupName: 'Working', lifeMinYears: null, lifeMaxYears: null, image: null }
];
const detail = {
  id: 'corgi', name: 'Pembroke Welsh Corgi', description: 'A bright, loyal companion.', hypoallergenic: false,
  group: { id: 'herding', name: 'Herding' }, origin: { country: 'Wales', region: null, era: null },
  life: { minYears: 12, maxYears: 13 }, maleWeight: { minKg: 10, maxKg: 14 }, femaleWeight: { minKg: null, maxKg: null },
  maleHeight: { minCm: 25, maxCm: 30 }, femaleHeight: { minCm: null, maxCm: null }, coat: { type: null, length: null, colors: [] },
  traits: { energy: 4, trainability: null, barking: null, grooming: null, shedding: null, drooling: null,
    goodWithChildren: null, goodWithDogs: null, goodWithStrangers: null, apartmentFriendly: null, exerciseMinutes: null, temperament: ['Playful'] },
  otherNames: ['Welsh Corgi'], recognizedBy: ['AKC'], sources: [], primaryImage: null, images: [], relatedBreeds: []
};

async function mockCatalog(page, { unavailable = false, empty = false, breed = detail, otherBreeds = [] } = {}) {
  const requests = [];
  await page.route('**/api/dogipedia/breeds**', async route => {
    const url = new URL(route.request().url());
    requests.push(url);
    if (url.pathname.endsWith('/corgi')) return route.fulfill({ json: breed });
    const other = otherBreeds.find(b => url.pathname.endsWith('/' + b.id));
    if (other) return route.fulfill({ json: other });
    if (unavailable) return route.fulfill({ status: 503, json: { code: 'dogipedia_catalog_unavailable' } });
    const n = Number(url.searchParams.get('page') || 1);
    const search = url.searchParams.get('search') || '';
    const items = empty ? [] : search ? cards.filter(card => card.name.toLowerCase().includes(search.toLowerCase())) : cards;
    await route.fulfill({ json: { items, page: n, pageSize: 24, totalItems: empty ? 0 : search ? items.length : 50,
      totalPages: empty ? 0 : search ? 1 : 3, hasPreviousPage: n > 1, hasNextPage: !search && n < 3 } });
  });
  return requests;
}

test('restores query state, debounces search, resets page, and supports Back and reload', async ({ page }) => {
  const requests = await mockCatalog(page);
  await page.goto('/dogipedia?page=3');
  await expect(page.getByRole('heading', { name: 'Pembroke Welsh Corgi', exact: true })).toBeVisible();
  await expect(page.getByText('Page 3 of 3')).toBeVisible();
  await page.getByRole('searchbox').fill('co');
  await page.waitForTimeout(100);
  expect(requests).toHaveLength(1);
  await page.getByRole('searchbox').fill('corgi');
  await expect(page).toHaveURL(/search=corgi&page=1/);
  await expect.poll(() => requests.length).toBe(2);
  await expect(page.getByRole('heading', { name: 'Akita', exact: true })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('searchbox')).toHaveValue('corgi');
  await page.goBack();
  await expect(page.getByText('Page 3 of 3')).toBeVisible();
  await expect(page.getByRole('searchbox')).toHaveValue('');
});

test('paginates, opens detail, preserves return state, and renders nullable measurements', async ({ page }) => {
  await mockCatalog(page);
  await page.goto('/dogipedia');
  await expect(page.getByRole('button', { name: 'Previous' })).toBeDisabled();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page).toHaveURL(/page=2/);
  await page.getByRole('link', { name: /Pembroke Welsh Corgi/ }).click();
  await expect(page).toHaveURL(/dogipedia\/breeds\/corgi.*page=2/);
  await expect(page.getByRole('heading', { name: 'Pembroke Welsh Corgi', exact: true })).toBeVisible();
  await expect(page.getByText('22–31 lb', { exact: true })).toBeVisible();
  await expect(page.getByText('10–12 in', { exact: true })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Energy: 4 out of 5' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'No photo available for Pembroke Welsh Corgi' })).toBeVisible();
  await expect(page.getByText('Not recorded', { exact: true })).toHaveCount(4);
  await page.getByRole('link', { name: 'Back to Dogipedia' }).click();
  await expect(page.getByText('Page 2 of 3')).toBeVisible();
});

test('distinguishes no results and catalog initialization', async ({ page }) => {
  await mockCatalog(page, { empty: true });
  await page.goto('/dogipedia?search=xyz');
  await expect(page.getByRole('heading', { name: 'No breeds found for “xyz”.' })).toBeVisible();
  await page.unrouteAll();
  await mockCatalog(page, { unavailable: true });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Dogipedia is getting its breed guide ready.' })).toBeVisible();
});

test('escapes upstream text, displays attribution, and replaces failed images', async ({ page }) => {
  const image = { id: 'photo-1', mediumUrl: 'https://photos.example/corgi.svg', thumbUrl: null, author: '<b>Jane</b>',
    license: 'CC BY-SA', licenseUrl: 'https://example.com/license', source: 'Wikimedia Commons', sourceUrl: 'javascript:alert(1)' };
  await page.route('https://photos.example/corgi.svg', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100"><rect width="200" height="100" fill="#c9d5c5"/></svg>' }));
  await mockCatalog(page, { breed: { ...detail, description: '<script>alert(1)</script>', primaryImage: image, images: [image] } });
  await page.goto('/dogipedia/breeds/corgi');
  await expect(page.getByText('<script>alert(1)</script>', { exact: true })).toBeVisible();
  await expect(page.getByText('Photo: <b>Jane</b>', { exact: false })).toBeVisible();
  await expect(page.getByRole('link', { name: 'CC BY-SA' })).toHaveAttribute('href', 'https://example.com/license');
  await expect(page.locator('a[href^="javascript:"]')).toHaveCount(0);
  await page.unroute('https://photos.example/corgi.svg');
  await page.route('https://photos.example/corgi.svg', route => route.abort());
  await page.reload();
  await expect(page.getByRole('img', { name: 'No photo available for Pembroke Welsh Corgi' })).toBeVisible();
});

test('cancels an older response and keeps the newest search results', async ({ page }) => {
  await mockCatalog(page);
  await page.goto('/dogipedia');
  await expect(page.getByRole('heading', { name: 'Akita', exact: true })).toBeVisible();
  await page.route('**/api/dogipedia/breeds?**', async route => {
    const search = new URL(route.request().url()).searchParams.get('search');
    if (search === 'corgi') await new Promise(resolve => setTimeout(resolve, 800));
    const items = search === 'akita' ? [cards[1]] : [cards[0]];
    await route.fulfill({ json: { items, page: 1, pageSize: 24, totalItems: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false } }).catch(() => {});
  });
  await page.getByRole('searchbox').fill('corgi');
  await expect(page).toHaveURL(/search=corgi/);
  await page.getByRole('searchbox').fill('akita');
  await expect(page).toHaveURL(/search=akita/);
  await expect(page.getByRole('heading', { name: 'Akita', exact: true })).toBeVisible();
  await page.waitForTimeout(900);
  await expect(page.getByRole('heading', { name: 'Pembroke Welsh Corgi', exact: true })).toHaveCount(0);
});

test('desktop and mobile layouts remain usable without horizontal overflow', async ({ page }, testInfo) => {
  await mockCatalog(page);
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/dogipedia');
    await expect(page.getByRole('heading', { name: 'Akita', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Dogipedia', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`browse-${width}.png`), fullPage: true });
    await page.getByRole('link', { name: /Pembroke Welsh Corgi/ }).click();
    await expect(page.getByRole('heading', { name: 'Size & lifespan' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`detail-${width}.png`), fullPage: true });
  }
});

function galleryImage(index) {
  return { id: `image-${index}`, thumbUrl: `https://photos.example/thumb-${index}.svg`,
    mediumUrl: `https://photos.example/medium-${index}.svg`, largeUrl: `https://photos.example/large-${index}.svg`,
    originalUrl: `https://photos.example/original-${index}.svg`, author: `Photographer ${index}`,
    license: `License ${index}`, licenseUrl: `https://example.com/license-${index}`,
    source: 'Wikimedia Commons', sourceUrl: `https://example.com/source-${index}` };
}

async function mockPhotos(page) {
  const requested = [];
  await page.route('https://photos.example/**', route => {
    requested.push(route.request().url());
    return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="280"><rect width="160" height="280" fill="#c9d5c5"/><circle cx="80" cy="85" r="42" fill="#49664a"/><rect x="35" y="140" width="90" height="105" rx="20" fill="#c35d3c"/></svg>' });
  });
  return requested;
}

test('gallery switches selected photo and credits locally with accessible keyboard controls', async ({ page }) => {
  const images = [galleryImage(1), galleryImage(2), galleryImage(3)];
  const photos = await mockPhotos(page);
  const requests = await mockCatalog(page, { breed: { ...detail, primaryImage: images[0], images } });
  await page.goto('/dogipedia/breeds/corgi');
  const gallery = page.getByRole('region', { name: 'Breed photos' });
  const main = gallery.getByRole('img', { name: detail.name, exact: true });
  await expect(main).toHaveAttribute('src', images[0].mediumUrl);
  await expect(main).toHaveAttribute('fetchpriority', 'high');
  await expect(main).toHaveCSS('object-fit', 'contain');
  await expect(gallery.locator('.gallery-thumbnails img')).toHaveCount(3);
  await expect(gallery.locator('.gallery-thumbnails img').first()).toHaveAttribute('src', images[0].thumbUrl);
  await expect(gallery.locator('.gallery-thumbnails img').first()).toHaveAttribute('loading', 'lazy');
  const previous = gallery.getByRole('button', { name: 'Previous photo', exact: true });
  const next = gallery.getByRole('button', { name: 'Next photo', exact: true });
  await expect(previous).toBeDisabled();
  await expect(next).toBeEnabled();
  await next.click();
  await expect(main).toHaveAttribute('src', images[1].mediumUrl);
  await expect(previous).toBeEnabled();
  await expect(gallery.locator('figcaption')).toContainText('Photographer 2');
  await next.click();
  await expect(main).toHaveAttribute('src', images[2].mediumUrl);
  await expect(next).toBeDisabled();
  await previous.click();
  await expect(main).toHaveAttribute('src', images[1].mediumUrl);
  await previous.click();
  await expect(main).toHaveAttribute('src', images[0].mediumUrl);
  await expect(previous).toBeDisabled();
  const second = gallery.getByRole('button', { name: `View image 2 of 3 for ${detail.name}` });
  await second.click();
  await expect(main).toHaveAttribute('src', images[1].mediumUrl);
  await expect(second).toHaveAttribute('aria-pressed', 'true');
  await expect(gallery.locator('figcaption')).toContainText('Photographer 2');
  await expect(gallery.locator('figcaption')).not.toContainText('Photographer 1');
  await expect(gallery.getByRole('link', { name: 'License 2' })).toHaveAttribute('href', images[1].licenseUrl);
  await second.press('ArrowRight');
  await expect(main).toHaveAttribute('src', images[2].mediumUrl);
  await expect(gallery.getByRole('button', { name: `View image 3 of 3 for ${detail.name}` })).toBeFocused();
  await page.keyboard.press('Home');
  await expect(main).toHaveAttribute('src', images[0].mediumUrl);
  await expect(gallery.getByRole('button', { name: `View image 1 of 3 for ${detail.name}` })).toHaveAttribute('aria-pressed', 'true');
  expect(requests).toHaveLength(1);
  expect(photos.some(url => url.includes('original-'))).toBe(false);
});

test('zero or one image omits the selector and empty related breeds stay hidden', async ({ page }) => {
  await mockPhotos(page);
  await mockCatalog(page);
  await page.goto('/dogipedia/breeds/corgi');
  await expect(page.getByRole('img', { name: `No photo available for ${detail.name}` })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Choose a breed photo' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Related Breeds', exact: true })).toHaveCount(0);
  await page.unroute('**/api/dogipedia/breeds**');
  const image = galleryImage(1);
  await mockCatalog(page, { breed: { ...detail, primaryImage: image, images: [image] } });
  await page.reload();
  await expect(page.getByRole('region', { name: 'Breed photos' }).getByRole('img', { name: detail.name, exact: true })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Choose a breed photo' })).toHaveCount(0);
});

test('failed gallery images use placeholders while other photos remain selectable', async ({ page }) => {
  await mockPhotos(page);
  await page.route('https://photos.example/*-2.svg', route => route.abort());
  const images = [galleryImage(1), galleryImage(2), galleryImage(3)];
  const requests = await mockCatalog(page, { breed: { ...detail, primaryImage: images[0], images } });
  await page.goto('/dogipedia/breeds/corgi');
  const gallery = page.getByRole('region', { name: 'Breed photos' });
  const second = gallery.getByRole('button', { name: `View image 2 of 3 for ${detail.name}` });
  await expect(second.getByRole('img', { name: 'Photo unavailable' })).toBeVisible();
  await second.click();
  await expect(gallery.getByRole('img', { name: `No photo available for ${detail.name}` })).toBeVisible();
  await gallery.getByRole('button', { name: `View image 3 of 3 for ${detail.name}` }).click();
  await expect(gallery.getByRole('img', { name: detail.name, exact: true })).toHaveAttribute('src', images[2].mediumUrl);
  await expect(gallery.locator('figcaption')).toContainText('Photographer 3');
  expect(requests).toHaveLength(1);
});

test('related cards navigate without reloading and reset the gallery while retaining browse state', async ({ page }) => {
  await mockPhotos(page);
  const images = [galleryImage(1), galleryImage(2)];
  const relatedBreeds = [
    { id: 'collie', name: 'Border Collie', groupName: 'Herding', image: galleryImage(3) },
    { id: 'shepherd', name: 'Australian Shepherd', groupName: 'Herding', image: null }
  ];
  const nextImage = galleryImage(4);
  const requests = await mockCatalog(page, { breed: { ...detail, primaryImage: images[0], images, relatedBreeds },
    otherBreeds: [{ ...detail, id: 'collie', name: 'Border Collie', primaryImage: nextImage, images: [nextImage] }] });
  await page.goto('/dogipedia/breeds/corgi?search=corgi&page=2');
  await page.getByRole('button', { name: `View image 2 of 2 for ${detail.name}` }).click();
  const related = page.getByRole('region', { name: 'Related Breeds' });
  await expect(related.locator('article')).toHaveCount(2);
  await expect(related.getByRole('img', { name: 'Border Collie', exact: true })).toBeVisible();
  await expect(related.getByRole('img', { name: 'No photo available for Australian Shepherd' })).toBeVisible();
  await expect(related.getByRole('link', { name: /Pembroke Welsh Corgi/ })).toHaveCount(0);
  let documentNavigations = 0;
  page.on('request', request => { if (request.resourceType() === 'document') documentNavigations++; });
  await related.getByRole('link', { name: /Border Collie/ }).click();
  await expect(page).toHaveURL(/\/breeds\/collie\?search=corgi&page=2/);
  await expect(page.getByRole('heading', { name: 'Border Collie', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Breed photos' }).getByRole('img', { name: 'Border Collie', exact: true })).toHaveAttribute('src', nextImage.mediumUrl);
  await expect(page.getByRole('group', { name: 'Choose a breed photo' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Related Breeds', exact: true })).toHaveCount(0);
  expect(documentNavigations).toBe(0);
  expect(requests).toHaveLength(2);
});

test('gallery carousel keeps selected thumbnails visible and related cards fit desktop and mobile', async ({ page }, testInfo) => {
  await mockPhotos(page);
  const images = Array.from({ length: 12 }, (_, i) => galleryImage(i + 1));
  const relatedBreeds = ['Australian Shepherd', 'Border Collie', 'Cardigan Welsh Corgi', 'German Shepherd Dog', 'Puli', 'Shetland Sheepdog']
    .map((name, index) => ({ id: `related-${index}`, name, groupName: 'Herding', image: index % 2 ? null : galleryImage(index + 1) }));
  await mockCatalog(page, { breed: { ...detail, primaryImage: images[0], images, relatedBreeds } });
  for (const width of [1280, 768, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/dogipedia/breeds/corgi');
    const strip = page.getByRole('group', { name: 'Choose a breed photo' });
    await expect(strip.getByRole('button')).toHaveCount(12);
    expect(await strip.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    await expect(strip).toHaveCSS('scrollbar-width', 'none');
    await strip.getByRole('button').first().press('End');
    await expect(strip.getByRole('button').last()).toBeFocused();
    await expect(strip.getByRole('button').last()).toHaveAttribute('aria-pressed', 'true');
    await expect.poll(() => strip.evaluate(element => {
      const last = element.querySelector('button:last-child').getBoundingClientRect();
      const frame = element.getBoundingClientRect();
      return last.left >= frame.left && last.right <= frame.right;
    })).toBe(true);
    await expect(page.getByRole('button', { name: 'Next photo', exact: true })).toBeDisabled();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByRole('region', { name: 'Breed photos' }).screenshot({ path: testInfo.outputPath(`gallery-${width}.png`) });
    const related = page.getByRole('region', { name: 'Related Breeds' });
    await expect(related.locator('article')).toHaveCount(6);
    await related.screenshot({ path: testInfo.outputPath(`related-${width}.png`) });
  }
});
