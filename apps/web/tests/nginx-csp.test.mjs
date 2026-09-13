import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

// Opt in to test the real runtime script and HTTP headers in a disposable container.
test('Nginx allows Dogipedia photos with no new environment setting and preserves the existing CSP', {
  skip: process.env.HOOVIEPACK_TEST_NGINX !== '1', timeout: 120_000
}, async () => {
  const name = `hooviepack-csp-test-${randomUUID()}`;
  const root = fileURLToPath(new URL('..', import.meta.url));
  const s3Origin = 'https://test-bucket.s3.us-east-2.amazonaws.com';
  const oidcOrigin = 'https://auth.example.test';
  const docker = (...args) => execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    docker('run', '--detach', '--name', name, '--add-host', 'api:127.0.0.1',
      '--publish', '127.0.0.1::80', '--mount', `type=bind,source=${root},target=/test,readonly`,
      '--env', `CSP_S3_ORIGIN=${s3Origin}`, '--env', `CSP_OIDC_ORIGIN=${oidcOrigin}`,
      '--entrypoint', 'sh', 'nginx:1.29-alpine', '-ec',
      "cp /test/nginx.conf /etc/nginx/conf.d/default.conf; mkdir -p /usr/share/nginx/html/assets; " +
      "sh /test/docker-entrypoint.d/40-runtime-config.sh; nginx -t; exec nginx -g 'daemon off;'");
    const port = docker('port', name, '80/tcp').split(':').at(-1);
    const base = `http://127.0.0.1:${port}`;
    let ready = false;
    for (let attempt = 0; attempt < 50; attempt++) {
      try { ready = (await fetch(`${base}/healthz`)).ok; } catch { /* Container is starting. */ }
      if (ready) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(ready, docker('logs', name));
    const config = docker('exec', name, 'cat', '/etc/nginx/conf.d/default.conf');
    assert.ok(config.includes('try_files $uri $uri/ /index.html'), 'Nginx runtime variables must survive envsubst');
    assert.ok(!config.includes('${CSP_'), 'All CSP placeholders must be resolved');
    for (const path of ['/', '/dogipedia', '/assets/config.json', '/assets/missing.png', '/api/csp-test']) {
      const response = await fetch(base + path);
      const policy = response.headers.get('content-security-policy');
      assert.ok(policy, `Missing CSP for ${path}`);
      const directives = Object.fromEntries(policy.split(';').filter(value => value.trim()).map(value => {
        const [name, ...sources] = value.trim().split(/\s+/);
        return [name, sources];
      }));
      assert.deepEqual(directives['img-src'], ["'self'", 'data:', 'blob:', s3Origin, 'https://images.dogapi.dog']);
      assert.deepEqual(directives['connect-src'], ["'self'", oidcOrigin, s3Origin]);
      assert.deepEqual(directives['default-src'], ["'self'"]);
      assert.deepEqual(directives['frame-ancestors'], ["'none'"]);
      await response.body?.cancel();
    }
  } catch (error) {
    let logs = '';
    try { const result = spawnSync('docker', ['logs', name], { encoding: 'utf8' }); logs = `${result.stdout}${result.stderr}`; } catch { /* Docker may not be available. */ }
    throw new Error(`${error.message}\n${logs}`, { cause: error });
  } finally {
    try { docker('rm', '--force', '--volumes', name); } catch { /* It may not have been created. */ }
  }
});
