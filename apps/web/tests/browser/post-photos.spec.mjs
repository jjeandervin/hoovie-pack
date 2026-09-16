import { test, expect } from '@playwright/test';

for (const width of [390, 1280]) {
  test(`portrait photos stay above usable reactions at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.route('**/test-photos/**', route => route.fulfill({
      contentType: 'image/svg+xml',
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="400" height="2400"><rect width="400" height="2400" fill="tan"/></svg>'
    }));
    await page.route('**/api/families/pack/posts?**', route => route.fulfill({ json: {
      items: [1, 2, 3, 4].map(count => ({
        id: `post-${count}`, familyId: 'pack', author: { id: 'jamie', displayName: 'Jamie' },
        content: `${count} portrait photos`, createdAt: '2026-09-15T12:00:00Z', comments: [], reactions: [],
        photos: Array.from({ length: count }, (_, i) => ({ id: `photo-${i}`, url: `/test-photos/${i}.svg` }))
      })), page: 1, pageSize: 10, totalCount: 4, hasMore: false
    } }));
    await page.goto('/feed');
    await expect(page.locator('.post-card')).toHaveCount(4);
    for (const card of await page.locator('.post-card').all()) {
      await card.scrollIntoViewIfNeeded();
      for (const photo of await card.locator('.post-photo img').all()) {
        await photo.scrollIntoViewIfNeeded();
        await expect.poll(() => photo.evaluate(img => img.complete && img.naturalHeight)).toBe(2400);
        await expect(photo).toHaveCSS('object-fit', 'contain');
        await expect(photo).toHaveCSS('transform', 'none');
      }
      const bar = card.locator('.reaction-bar');
      await bar.scrollIntoViewIfNeeded();
      const barBox = await bar.boundingBox();
      for (const photo of await card.locator('.post-photo').all()) {
        const box = await photo.boundingBox();
        expect(box.y + box.height).toBeLessThanOrEqual(barBox.y + 1);
      }
      await card.locator('.comment-toggle').click();
      await expect(card.getByRole('textbox', { name: 'Write a comment' })).toBeVisible();
    }
  });
}
