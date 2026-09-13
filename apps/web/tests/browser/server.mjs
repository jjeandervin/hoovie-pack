import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { execFileSync } from 'node:child_process';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
execFileSync(process.execPath, [resolve(root, 'node_modules/@angular/compiler-cli/bundles/src/bin/ngc.js'), '-p', resolve(root, 'tests/browser/tsconfig.json')], { stdio: 'inherit' });
const result = await build({
  entryPoints: [resolve(root, '.angular/dogipedia-tests/tests/browser/main.js')], bundle: true, write: false,
  format: 'esm', target: 'es2022'
});

createServer(async (request, response) => {
  const path = new URL(request.url, 'http://localhost').pathname;
  if (path === '/app.js') { response.setHeader('Content-Type', 'text/javascript'); response.end(result.outputFiles[0].contents); return; }
  if (path === '/styles.css') { response.setHeader('Content-Type', 'text/css'); response.end(await readFile(resolve(root, 'src/styles.css'))); return; }
  if (/^\/assets\/[a-z0-9-]+\.(svg|png)$/.test(path)) {
    response.setHeader('Content-Type', path.endsWith('.svg') ? 'image/svg+xml' : 'image/png');
    try { response.end(await readFile(resolve(root, 'public' + path))); }
    catch { response.statusCode = 404; response.end(); }
    return;
  }
  response.setHeader('Content-Type', 'text/html');
  response.end('<!doctype html><html><head><base href="/"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Dogipedia test</title><link rel="stylesheet" href="/styles.css"></head><body><hp-test-root></hp-test-root><script type="module" src="/app.js"></script></body></html>');
}).listen(4317, '127.0.0.1');
