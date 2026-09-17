import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';
import '@angular/compiler';
import { createEnvironmentInjector, runInInjectionContext } from '@angular/core';
import { HttpErrorResponse, HttpRequest } from '@angular/common/http';
import { firstValueFrom, of, Subject, throwError } from 'rxjs';

const dataUrl = (source) => `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const authModule = dataUrl('export class AuthService {}');
const configModule = dataUrl('export class RuntimeConfigService {}');
const { AuthService } = await import(authModule);
const { RuntimeConfigService } = await import(configModule);

async function loadSource(file) {
  const source = await readFile(new URL(`../src/app/core/${file}`, import.meta.url), 'utf8');
  const { outputText } = ts.transpileModule(source, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022, experimentalDecorators: true }
  });
  const compiled = outputText.replace(/from '([^']+)'/g, (_, specifier) => {
    const url = specifier === './auth.service' ? authModule
      : specifier === './runtime-config.service' ? configModule
      : import.meta.resolve(specifier);
    return `from ${JSON.stringify(url)}`;
  });
  return import(dataUrl(compiled));
}

const { CurrentUserService } = await loadSource('current-user.service.ts');
const { ActiveFamilyService } = await loadSource('active-family.service.ts');
const { authInterceptor } = await loadSource('auth.interceptor.ts');

for (const [Service, method, result] of [
  [CurrentUserService, 'getMe', { id: 'user-1', displayName: 'Sam' }],
  [ActiveFamilyService, 'listFamilies', [{ id: 'family-1', name: 'Pack' }]]
]) {
  test(`${Service.name} retries failed loads and caches successful loads`, async () => {
    let calls = 0;
    const error = new Error('temporarily unavailable');
    const service = new Service({ [method]: () => ++calls === 1 ? throwError(() => error) : of(result) });
    const first = service.load();
    assert.equal(service.load(), first);
    await assert.rejects(first, error);
    assert.equal(service.loading(), false);
    assert.deepEqual(await service.load(), result);
    assert.deepEqual(await service.load(), result);
    assert.equal(calls, 2);
  });

  test(`${Service.name} keeps a newer pending load when an older load fails`, async () => {
    const older = new Subject();
    const newer = new Subject();
    let calls = 0;
    const service = new Service({ [method]: () => ++calls === 1 ? older : newer });
    const first = service.load();
    const rejection = assert.rejects(first, /older failed/);
    const second = service.load(true);
    older.error(new Error('older failed'));
    await rejection;
    assert.equal(service.load(), second);
    assert.equal(service.loading(), true);
    newer.next(result);
    assert.deepEqual(await second, result);
    assert.equal(service.loading(), false);
  });
}

for (const [url, status, shouldLogin] of [
  ['/api/families/1/posts', 401, true],
  ['/api/me', 0, false],
  ['/api/me', 403, false],
  ['/api/me', 500, false],
  ['https://storage.example/photo', 401, false]
]) {
  test(`request ${url} with status ${status} recovers only API authentication failures`, async (t) => {
    const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
    Object.defineProperty(globalThis, 'window', {
      configurable: true, value: { location: new URL('https://app.example/feed?view=family#latest') }
    });
    t.after(() => {
      if (previousWindow) Object.defineProperty(globalThis, 'window', previousWindow);
      else delete globalThis.window;
    });
    const login = t.mock.fn();
    const injector = createEnvironmentInjector([
      { provide: AuthService, useValue: { accessToken: () => 'expired-token', login } },
      { provide: RuntimeConfigService, useValue: { isApiUrl: value => value.startsWith('/api/') } }
    ]);
    t.after(() => injector.destroy());
    const error = new HttpErrorResponse({ status });
    let outgoing;
    const response = runInInjectionContext(injector, () => authInterceptor(new HttpRequest('GET', url), request => {
      outgoing = request;
      return throwError(() => error);
    }));
    await assert.rejects(firstValueFrom(response), value => value === error);
    assert.equal(login.mock.callCount(), shouldLogin ? 1 : 0);
    if (shouldLogin) assert.deepEqual(login.mock.calls[0].arguments, ['/feed?view=family#latest']);
    assert.equal(outgoing.headers.get('Authorization'), url.startsWith('/api/') ? 'Bearer expired-token' : null);
  });
}
