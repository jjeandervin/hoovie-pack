import assert from 'node:assert/strict';
import test from 'node:test';
import { isUrlWithinBase } from '../src/app/core/api-url.ts';

const appOrigin = 'https://app.example';

test('accepts only the configured same-origin API path boundary', () => {
  assert.equal(isUrlWithinBase('/api', '/api', appOrigin), true);
  assert.equal(isUrlWithinBase('/api/families', '/api', appOrigin), true);
  assert.equal(isUrlWithinBase('https://app.example/api/posts/1', '/api', appOrigin), true);
  assert.equal(isUrlWithinBase('/api.evil/collect', '/api', appOrigin), false);
  assert.equal(isUrlWithinBase('/api-v2/collect', '/api', appOrigin), false);
  assert.equal(isUrlWithinBase('//evil.example/api/collect', '/api', appOrigin), false);
});

test('rejects lookalike origins for an absolute API base URL', () => {
  const apiBase = 'https://api.example/v1';

  assert.equal(isUrlWithinBase('https://api.example/v1/families', apiBase, appOrigin), true);
  assert.equal(isUrlWithinBase('https://api.example/v10/collect', apiBase, appOrigin), false);
  assert.equal(isUrlWithinBase('https://api.example.evil/v1/collect', apiBase, appOrigin), false);
  assert.equal(isUrlWithinBase('http://api.example/v1/collect', apiBase, appOrigin), false);
});
