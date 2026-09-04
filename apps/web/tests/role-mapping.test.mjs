import assert from 'node:assert/strict';
import test from 'node:test';
import { mapMembershipRole } from '../src/app/core/role-mapping.ts';

test('maps camel-case API membership roles', () => {
  assert.equal(mapMembershipRole('owner'), 'Owner');
  assert.equal(mapMembershipRole('admin'), 'Admin');
  assert.equal(mapMembershipRole('member'), 'Member');
});

test('continues to support named and numeric membership roles', () => {
  assert.equal(mapMembershipRole('Owner'), 'Owner');
  assert.equal(mapMembershipRole('Admin'), 'Admin');
  assert.equal(mapMembershipRole('Member'), 'Member');
  assert.equal(mapMembershipRole(0), 'Owner');
  assert.equal(mapMembershipRole(1), 'Admin');
  assert.equal(mapMembershipRole(2), 'Member');
});
