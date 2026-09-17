import { test, expect } from '@playwright/test';

test('member and dog birthdays share month and timeline views and link to profiles', async ({ page }) => {
  const items = [
    { id: 'member-birthday-member-1-2026', sourceType: 'memberBirthday', title: "Jamie's Birthday", eventType: 'Birthday', startDate: '2026-09-20', memberId: 'member-1', imageUrl: '/test-photos/member.svg' },
    { id: 'dog-birthday-dog-1-2026', sourceType: 'dogBirthday', title: "Hoovie's Birthday", eventType: 'DogBirthday', startDate: '2026-09-20', dogId: 'dog-1', imageUrl: '/test-photos/dog.svg' }
  ].map(item => ({ endDate: null, isAllDay: true, startTime: null, calendarEventId: null, isEditable: false, photos: [], ...item }));
  await page.route('**/api/families/pack/calendar-events?**', route => route.fulfill({ json: { items, page: 1, totalCount: 2, totalPages: 1 } }));
  await page.route('**/test-photos/**', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><rect width="40" height="40" fill="tan"/></svg>' }));
  await page.route('**/api/families/pack/members', route => route.fulfill({ json: [{ membershipId: 'member-1', userId: 'jamie', displayName: 'Jamie', role: 'member' }] }));
  await page.goto('/tools/calendar');
  await page.getByLabel('Jump to month').fill('2026-09');
  await page.getByRole('button', { name: '2026-09-20, 2 events', exact: true }).click();
  const agenda = page.getByRole('region', { name: 'Selected day events' });
  await expect(agenda.getByRole('link', { name: /Jamie's Birthday/ })).toHaveAttribute('href', '/members/member-1');
  await expect(agenda.getByRole('link', { name: /Hoovie's Birthday/ })).toHaveAttribute('href', '/dogs/dog-1');
  await expect(agenda.locator('img')).toHaveCount(2);
  await expect(agenda).toContainText('🎂');
  await expect(agenda).toContainText('🐾');
  await agenda.getByRole('link', { name: /Jamie's Birthday/ }).click();
  await expect(page.getByRole('heading', { name: 'Jamie', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Edit profile', exact: true })).toHaveAttribute('href', '/profile');
  await page.goto('/tools/calendar');
  await page.getByRole('button', { name: 'Timeline / List', exact: true }).click();
  await expect(page.getByRole('link', { name: /Hoovie's Birthday/ })).toHaveAttribute('href', '/dogs/dog-1');
  await page.getByRole('combobox', { name: 'Browse', exact: true }).selectOption('history');
  await expect(page.getByRole('link', { name: /Jamie's Birthday/ })).toBeVisible();
});

test('profile birthday can be added, loaded again, changed and cleared', async ({ page }) => {
  let profile = { id: 'jamie', email: 'jamie@example.test', displayName: 'Jamie', bio: '', birthDate: null };
  await page.route('**/api/me', async route => {
    if (route.request().method() === 'PUT') profile = { ...profile, ...route.request().postDataJSON() };
    await route.fulfill({ json: profile });
  });
  await page.goto('/profile');
  const birthday = page.getByLabel('Birthday Optional');
  await birthday.fill('2000-02-29');
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect.poll(() => profile.birthDate).toBe('2000-02-29');
  await page.reload();
  await expect(birthday).toHaveValue('2000-02-29');
  await birthday.fill('1983-05-14');
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect.poll(() => profile.birthDate).toBe('1983-05-14');
  await birthday.fill('');
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect.poll(() => profile.birthDate).toBeNull();
});
const original = { sourceType: 'calendarEvent', calendarEventId: 'event-1', memberId: null, dogId: null, imageUrl: null, isEditable: true, id: 'event-1', familyId: 'pack', title: 'Myrtle Beach', description: 'The little yellow house.', eventType: 'Vacation', startDate: '1997-06-14', endDate: '1997-06-21', isAllDay: true, startTime: null, endTime: null, timeZoneId: null, location: 'Myrtle Beach, SC', createdBy: { id: 'jamie', displayName: 'Jamie' }, createdAtUtc: '2026-09-16T12:00:00Z', updatedAtUtc: '2026-09-16T12:00:00Z', canManage: true, photos: [] };
async function mock(page, overrides = {}) {
  let event = { ...original, ...overrides }; const requests = [];
  await page.route('**/api/families/pack/calendar-events**', async route => {
    const request = route.request(), url = new URL(request.url()); requests.push({ method: request.method(), url, body: request.postDataJSON() });
    if (url.pathname.endsWith('/calendar-events') && request.method() === 'GET') return route.fulfill({ json: { items: [event], page: 1, totalCount: 1, totalPages: 1 } });
    if (request.method() === 'DELETE') return route.fulfill({ status: 204 });
    if (request.method() === 'POST' && url.pathname.endsWith('/photos')) { event = { ...event, photos: [...event.photos, { id: 'photo-1', fileId: 'file-1', url: '/test-photos/beach.svg', isCover: true, sortOrder: 0 }] }; return route.fulfill({ json: event }); }
    if (['POST', 'PUT'].includes(request.method())) event = { ...event, ...request.postDataJSON() };
    return route.fulfill({ json: event });
  });
  return requests;
}
for (const width of [390, 1280]) {
  test(`calendar historical navigation, selected date and details at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); const requests = await mock(page);
    await page.goto('/tools'); await page.getByRole('link', { name: /Family Calendar/ }).click();
    await page.getByLabel('Jump to month').fill('1997-06');
    await expect(page.getByRole('heading', { name: 'June 1997', exact: true })).toBeVisible();
    await page.getByRole('button', { name: '1997-06-14, 1 events', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'June 14, 1997' })).toBeVisible();
    expect(requests.some(r => r.url.searchParams.get('startDate') === '1997-06-01' && r.url.searchParams.get('endDate') === '1997-06-30')).toBeTruthy();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await page.screenshot({ path: `test-results/calendar-month-${width}.png`, fullPage: true });
    await expect(page.getByRole('link', { name: '+ Add event on this date' })).toHaveAttribute('href', '/tools/calendar/events/new?date=1997-06-14');
    await page.getByRole('link', { name: '+ Add Event', exact: true }).click();
    await expect(page.getByLabel('Start date', { exact: true })).toHaveValue('1997-06-14');
    await page.getByLabel('Title', { exact: true }).fill('A family memory');
    await page.getByRole('button', { name: 'Save event', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'A family memory' })).toBeVisible();
    await expect(page.getByText(/Created Sep 16, 2026/)).toBeVisible();
    await page.screenshot({ path: `test-results/calendar-detail-${width}.png`, fullPage: true });
  });
}
test('timeline switches query direction and opens event details', async ({ page }) => {
  const requests = await mock(page); await page.goto('/tools/calendar');
  await page.getByRole('button', { name: 'Timeline / List', exact: true }).click();
  await page.getByRole('combobox', { name: 'Browse', exact: true }).selectOption('history');
  await expect(page.getByRole('heading', { name: 'June 1997' })).toBeVisible();
  expect(requests.some(r => r.url.searchParams.get('direction') === 'history')).toBeTruthy();
  await page.getByRole('link', { name: /Myrtle Beach/ }).click();
  await expect(page.getByText('The little yellow house.')).toBeVisible();
});
test('edit validates ranges, supports timed events and confirms deletion', async ({ page }) => {
  const requests = await mock(page); await page.goto('/tools/calendar/events/event-1/edit');
  await page.getByLabel('End date (optional)', { exact: true }).fill('1997-06-01');
  await page.getByRole('button', { name: 'Save event', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('End date cannot precede');
  await page.getByLabel('End date (optional)', { exact: true }).fill('1997-06-14');
  await page.getByLabel('All day', { exact: true }).uncheck();
  await page.getByLabel('Start time', { exact: true }).fill('17:00');
  await page.getByLabel('End time (optional)', { exact: true }).fill('18:00');
  await page.getByRole('button', { name: 'Save event', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Myrtle Beach' })).toBeVisible();
  expect(requests.find(r => r.method === 'PUT').body.isAllDay).toBe(false);
  await page.getByRole('button', { name: 'Delete event', exact: true }).click();
  expect(requests.filter(r => r.method === 'DELETE')).toHaveLength(0);
  await page.getByRole('button', { name: 'Yes, delete event', exact: true }).click();
  await expect(page).toHaveURL(/tools\/calendar$/);
  expect(requests.filter(r => r.method === 'DELETE')).toHaveLength(1);
});
test('read-only members see photos and lightbox without management controls', async ({ page }) => {
  await mock(page, { canManage: false, photos: [{ id: 'p', fileId: 'f', url: '/test-photos/beach.svg', isCover: true }] });
  await page.route('**/test-photos/**', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300"><rect width="400" height="300" fill="tan"/></svg>' }));
  await page.goto('/tools/calendar/events/event-1');
  await expect(page.getByRole('heading', { name: 'Myrtle Beach' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Edit event' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Delete event', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Enlarge event photo' }).click();
  await expect(page.getByRole('dialog')).toBeVisible(); await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).not.toBeVisible();
});
test('new photos use presigned upload before associating file references', async ({ page }) => {
  const requests = await mock(page);
  await page.route('**/api/media/uploads', route => route.fulfill({ json: { fileId: 'file-1', uploadToken: 'token-1', uploadUrl: 'http://127.0.0.1:4317/test-upload', requiredHeaders: {} } }));
  await page.route('**/test-upload', route => route.fulfill({ status: 200 }));
  await page.goto('/tools/calendar/events/event-1');
  await page.locator('input[type=file]').setInputFiles({ name: 'beach.png', mimeType: 'image/png', buffer: Buffer.from('photo') });
  await page.getByRole('button', { name: 'Attach photos' }).click();
  await expect(page.getByText('Cover photo', { exact: true })).toBeVisible();
  expect(requests.find(r => r.url.pathname.endsWith('/photos')).body).toEqual({ fileId: 'file-1', uploadToken: 'token-1' });
});
