import { test, expect } from '@playwright/test';

async function goalRoute(page) {
  // Inspect the logical board, then exercise actual keyboard/touch input for every step.
  return page.locator('hp-fetch-quest-page').evaluate(host => {
    const { maze, playerPosition } = window.ng.getComponent(host).state();
    const queue = [[playerPosition, []]], seen = new Set();
    for (const [p, route] of queue) {
      const key = `${p.row},${p.column}`;
      if (seen.has(key)) continue;
      seen.add(key);
      if (p.row === maze.goal.row && p.column === maze.goal.column) return route;
      for (const [direction, row, column] of [['up', -1, 0], ['down', 1, 0], ['left', 0, -1], ['right', 0, 1]]) {
        const next = { row: p.row + row, column: p.column + column };
        if (maze.cells[next.row]?.[next.column] === 0) queue.push([next, [...route, direction]]);
      }
    }
    throw new Error('No route to the ball');
  });
}

test('Tools opens Fetch Quest; keyboard wins, replay resets, next maze increases difficulty', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto('/tools');
  await page.getByRole('link', { name: /Fetch Quest/ }).click();
  await expect(page).toHaveURL(/\/tools\/fetch-quest$/);
  const canvas = page.locator('canvas'); await expect(canvas).toBeVisible();
  const moves = page.locator('.game-shell .hud div').filter({ has: page.locator('dt', { hasText: 'Moves' }) }).locator('dd');
  await page.getByRole('button', { name: 'Restart Maze' }).focus(); await page.keyboard.press('ArrowDown');
  await expect(moves).toHaveText('0');
  await canvas.focus(); await page.keyboard.press('ArrowUp'); await expect(moves).toHaveText('0');
  const route = await goalRoute(page);
  const arrow = { up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight' };
  const wasd = { up: 'w', down: 's', left: 'a', right: 'd' };
  for (let i = 0; i < route.length; i++) await page.keyboard.press((i % 2 ? arrow : wasd)[route[i]]);
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(moves).toHaveText(String(route.length));
  await expect(page.getByRole('button', { name: 'Next Maze' })).toBeFocused();
  await page.screenshot({ path: 'test-results/fetch-quest-victory.png', fullPage: true });
  await page.getByRole('button', { name: 'Replay Level' }).click();
  await expect(canvas).toBeFocused(); await expect(moves).toHaveText('0');
  expect(await goalRoute(page)).toEqual(route);
  for (const d of route) await page.keyboard.press(arrow[d]);
  await page.getByRole('button', { name: 'Next Maze' }).click();
  await expect(page.locator('.game-top')).toContainText('Level 2');
  await expect(page.locator('.game-top')).toContainText('15 × 15');
  await expect(moves).toHaveText('0');
  await page.screenshot({ path: 'test-results/fetch-quest-desktop.png', fullPage: true });
  await page.getByRole('link', { name: 'Back to tools' }).click();
  expect(errors).toEqual([]);
});

test('320px touch controls complete a maze without overflow and remain usable at maximum size', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 320, height: 740 }, isMobile: true, hasTouch: true });
  const page = await context.newPage(); await page.goto('/tools/fetch-quest');
  const route = await goalRoute(page);
  for (const direction of ['up', 'down', 'left', 'right']) {
    const box = await page.getByRole('button', { name: `Move ${direction}` }).boundingBox();
    expect(box.width).toBeGreaterThanOrEqual(44); expect(box.height).toBeGreaterThanOrEqual(44);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  for (const direction of route) await page.getByRole('button', { name: `Move ${direction}` }).tap();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('button', { name: 'Next Maze' }).tap();
  // Exercise progression through real controls to check the largest responsive board.
  for (let level = 2; level < 4; level++) {
    await page.locator('canvas').focus();
    for (const direction of await goalRoute(page)) await page.keyboard.press({ up: 'w', down: 's', left: 'a', right: 'd' }[direction]);
    await page.getByRole('button', { name: 'Next Maze' }).tap();
  }
  await expect(page.locator('.game-top')).toContainText('21 × 21');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/fetch-quest-mobile.png', fullPage: true });
  await context.close();
});
