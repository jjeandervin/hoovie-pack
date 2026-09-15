import assert from 'node:assert/strict';
import test from 'node:test';
import { searchState, searchParams, filterChips, clearFilters } from '../src/app/features/dogipedia/dogipedia-search.ts';

test('advanced state round trips repeated parameters and preserves canonical measurements', () => {
  const state = searchState(new URLSearchParams('search=collie&page=2&pageSize=12&energyMin=2&energyMax=4&coatLengths=short&coatLengths=medium&coatLengths=SHORT&temperaments=Friendly&hypoallergenicOnly=true&adultWeightMaxKg=22.679619'));
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(searchParams(state))) for (const item of Array.isArray(value) ? value : [value]) params.append(key, item);
  assert.deepEqual(searchState(params), state);
  assert.deepEqual(state.coatLengths, ['short', 'medium']);
  assert.equal(state.adultWeightMaxKg, 22.679619);
  assert.equal(filterChips(state, null).length, 6);
  assert.match(filterChips(state, null).find(c => c.id === 'adultWeightMinKg').label, /50 lb/);
  assert.deepEqual(clearFilters(state), { search: 'collie', page: 1, pageSize: 12, sort: 'name' });
});

test('invalid URL values normalize without inventing active filters', () => {
  const state = searchState(new URLSearchParams('energyMin=4&energyMax=2&barkingMax=9&exerciseMinMinutes=-1&coatTypes=+&hypoallergenicOnly=false&page=NaN&pageSize=500'));
  assert.deepEqual(state, { search: '', page: 1, pageSize: 24, sort: 'name' });
  assert.deepEqual(filterChips(state, null), []);
});


test('text search and page defaults retain previous browse behavior', () => {
  assert.deepEqual(searchState(new URLSearchParams('search=+corgi+&page=3')), { search: 'corgi', page: 3, pageSize: 24, sort: 'name' });
  for (const page of ['0', '-1', 'NaN', '1.5', '999999999999999']) {
    assert.equal(searchState(new URLSearchParams({ page })).page, 1);
  }
  assert.equal(searchState(new URLSearchParams({ search: 'a'.repeat(200) })).search.length, 100);
});
