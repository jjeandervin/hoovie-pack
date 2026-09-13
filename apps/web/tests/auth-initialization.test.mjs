import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

// Compile Angular decorators using the application's TypeScript settings.
const source = await readFile(new URL('../src/app/core/auth.service.ts', import.meta.url), 'utf8');
const { outputText } = ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022, experimentalDecorators: true }
});
const moduleSource = outputText.replace("'@angular/core'", JSON.stringify(import.meta.resolve('@angular/core')));
const { AuthService } = await import(`data:text/javascript;base64,${Buffer.from(moduleSource).toString('base64')}`);

const inviteUrl = '/onboarding?code=mCqp_f7qcVLMvc4PJCr5Bms__LcZJawohoJY0AOj2ZY';

function setup(t, path, { authenticated = false, callbackUri } = {}) {
  const location = new URL(path, 'http://localhost:4200');
  const storage = new Map();
  const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
  const previousStorage = Object.getOwnPropertyDescriptor(globalThis, 'sessionStorage');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: { location } });
  Object.defineProperty(globalThis, 'sessionStorage', {
    configurable: true,
    value: {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, value),
      removeItem: (key) => storage.delete(key)
    }
  });
  t.after(() => {
    if (previousWindow) Object.defineProperty(globalThis, 'window', previousWindow);
    else delete globalThis.window;
    if (previousStorage) Object.defineProperty(globalThis, 'sessionStorage', previousStorage);
    else delete globalThis.sessionStorage;
  });
  const oauth = {
    events: { subscribe() {} },
    configure() {},
    setStorage() {},
    loadDiscoveryDocument: t.mock.fn(async () => {}),
    tryLogin: t.mock.fn(async () => {
      // OIDC processing consumes `code` before the router sees the URL.
      location.searchParams.delete('code');
    }),
    async loadDiscoveryDocumentAndTryLogin() {
      await this.loadDiscoveryDocument();
      await this.tryLogin();
    },
    setupAutomaticSilentRefresh: t.mock.fn(),
    hasValidAccessToken: () => authenticated,
    getIdentityClaims: () => ({ name: 'Pack member' }),
    initCodeFlow: t.mock.fn()
  };
  const auth = new AuthService(oauth, {
    settings: () => ({ oidcIssuer: 'http://localhost:8081', oidcClientId: 'test', oidcRedirectUri: callbackUri })
  });
  return { auth, oauth, location };
}

for (const authenticated of [false, true]) {
  test(`preserves an invite link during initialization (authenticated: ${authenticated})`, async (t) => {
    const { auth, oauth, location } = setup(t, inviteUrl, { authenticated });
    await auth.initialize();

    assert.equal(location.pathname + location.search, inviteUrl);
    assert.equal(oauth.tryLogin.mock.callCount(), 0);
    assert.equal(oauth.loadDiscoveryDocument.mock.callCount(), 1);
    assert.equal(oauth.setupAutomaticSilentRefresh.mock.callCount(), 1);
    assert.equal(auth.initialized(), true);
    assert.equal(auth.isAuthenticated(), authenticated);
    assert.equal(auth.authError(), null);

    if (!authenticated) {
      auth.register(location.pathname + location.search);
      assert.deepEqual(oauth.initCodeFlow.mock.calls[0].arguments, ['', { prompt: 'create' }]);
      assert.equal(auth.consumeReturnUrl(), inviteUrl);
      assert.equal(auth.consumeReturnUrl(), '/feed');
    }
  });
}

test('processes authorization responses on the default callback route', async (t) => {
  const { auth, oauth, location } = setup(t, '/auth/callback?code=oauth-code&state=nonce');
  await auth.initialize();
  assert.equal(oauth.tryLogin.mock.callCount(), 1);
  assert.equal(location.searchParams.has('code'), false);
});

test('uses the configured callback route', async (t) => {
  const { auth, oauth } = setup(t, '/custom/callback?code=oauth-code&state=nonce', {
    callbackUri: 'http://localhost:4200/custom/callback'
  });
  await auth.initialize();
  assert.equal(oauth.tryLogin.mock.callCount(), 1);
});

test('leaves unrelated query parameters intact on ordinary routes', async (t) => {
  const { auth, oauth, location } = setup(t, '/feed?code=application-code&state=filter');
  await auth.initialize();
  assert.equal(oauth.tryLogin.mock.callCount(), 0);
  assert.equal(location.search, '?code=application-code&state=filter');
});
