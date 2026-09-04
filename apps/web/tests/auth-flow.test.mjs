import assert from 'node:assert/strict';
import test from 'node:test';
import { isFamilyInviteReturnUrl } from '../src/app/core/auth-flow.ts';

test('recognizes onboarding links containing an invite code', () => {
  assert.equal(isFamilyInviteReturnUrl('/onboarding?code=HERMES-7P4K'), true);
  assert.equal(isFamilyInviteReturnUrl('/onboarding?invite=https%3A%2F%2Fapp.example%2Finvite%2FABC'), true);
});

test('does not turn ordinary or unsafe return URLs into registration flows', () => {
  assert.equal(isFamilyInviteReturnUrl('/onboarding'), false);
  assert.equal(isFamilyInviteReturnUrl('/feed?code=HERMES-7P4K'), false);
  assert.equal(isFamilyInviteReturnUrl('//evil.example/onboarding?code=HERMES-7P4K'), false);
  assert.equal(isFamilyInviteReturnUrl('https://evil.example/onboarding?code=HERMES-7P4K'), false);
});
