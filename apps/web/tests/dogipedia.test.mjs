import assert from 'node:assert/strict';
import test from 'node:test';
import { browseState, measurement, safeExternalUrl } from '../src/app/features/dogipedia/dogipedia.helpers.ts';

test('browse state restores a bookmark and normalizes invalid pages', () => {
  assert.deepEqual(browseState(new URLSearchParams('search=+corgi+&page=3')), { search: 'corgi', page: 3 });
  for (const page of ['0', '-1', 'NaN', '1.5', '999999999999999']) {
    assert.equal(browseState(new URLSearchParams({ page })).page, 1);
  }
  assert.equal(browseState(new URLSearchParams({ search: 'a'.repeat(200) })).search.length, 100);
});

test('measurements convert units and preserve genuinely missing bounds', () => {
  assert.equal(measurement(10, 14, 'lb', 2.2046226218), '22–31 lb');
  assert.equal(measurement(25, 30, 'in', 1 / 2.54), '10–12 in');
  assert.equal(measurement(null, null, 'lb'), 'Not recorded');
  assert.equal(measurement(null, 12, 'years'), 'Up to 12 years');
  assert.equal(measurement(10, null, 'years'), 'From 10 years');
  assert.equal(measurement(12, 12, 'years'), '12 years');
});

test('external links reject unsafe protocols and relative addresses', () => {
  for (const url of ['javascript:alert(1)', 'data:text/html,hi', '//example.com', '/foo', 'file:///foo']) {
    assert.equal(safeExternalUrl(url), null);
  }
  assert.equal(safeExternalUrl('https://example.com/photo'), 'https://example.com/photo');
});
