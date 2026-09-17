import test from 'node:test';
import assert from 'node:assert/strict';
import { dateKey, localDate, monthDays, occursOn } from '../src/app/features/calendar/calendar.models.ts';
test('historical date-only values round trip without timezone shifts or a 1900 offset', () => {
  for (const date of ['0001-01-01', '0097-06-01', '1997-06-14', '2012-06-09', '2026-09-16']) assert.equal(dateKey(localDate(date)), date);
});
test('month calendars handle leap years and historical weekday alignment', () => {
  assert.equal(monthDays('1997-06')[0], '1997-06-01');
  assert.equal(monthDays('2000-02').filter(Boolean).length, 29);
  assert.equal(monthDays('1900-02').filter(Boolean).length, 28);
});
test('multi-day events include both endpoints and only their occurrence dates', () => {
  const e = { startDate: '1997-05-29', endDate: '1997-06-03' };
  assert.equal(occursOn(e, '1997-06-01'), true); assert.equal(occursOn(e, '1997-06-03'), true);
  assert.equal(occursOn(e, '1997-06-04'), false); assert.equal(occursOn(e, '1997-05-28'), false);
  assert.equal(occursOn({ startDate: '1997-06-01', endDate: null }, '1997-06-02'), false);
});
