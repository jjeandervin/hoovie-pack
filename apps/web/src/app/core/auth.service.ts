import { Injectable, computed, signal } from '@angular/core';
import { AuthConfig, OAuthEvent, OAuthService } from 'angular-oauth2-oidc';
import { RuntimeConfigService } from './runtime-config.service';

interface IdentityClaims {
  sub?: string;
  name?: string;
  preferred_username?: string;
  email?: string;
}

const RETURN_URL_KEY = 'hooviepack.returnUrl';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authenticatedSignal = signal(false);
  private readonly initializedSignal = signal(false);
  private readonly authErrorSignal = signal<string | null>(null);
  private readonly claimsSignal = signal<IdentityClaims>({});

  readonly isAuthenticated = this.authenticatedSignal.asReadonly();
  readonly initialized = this.initializedSignal.asReadonly();
  readonly authError = this.authErrorSignal.asReadonly();
  readonly claims = this.claimsSignal.asReadonly();
  readonly displayName = computed(
    () => this.claimsSignal().name || this.claimsSignal().preferred_username || 'Pack member'
  );

  constructor(
    private readonly oauth: OAuthService,
    private readonly runtimeConfig: RuntimeConfigService
  ) {
    this.oauth.events.subscribe((event: OAuthEvent) => {
      if (['token_received', 'token_refreshed', 'logout', 'session_terminated', 'token_error'].includes(event.type)) {
        this.syncState();
      }
    });
  }

  async initialize(): Promise<void> {
    const settings = this.runtimeConfig.settings();
    const callbackUri = settings.oidcRedirectUri || `${window.location.origin}/auth/callback`;
    const config: AuthConfig = {
      issuer: settings.oidcIssuer,
      redirectUri: callbackUri,
      postLogoutRedirectUri: settings.oidcPostLogoutRedirectUri || `${window.location.origin}/login`,
      clientId: settings.oidcClientId,
      responseType: 'code',
      scope: 'openid profile email',
      requireHttps: 'remoteOnly',
      strictDiscoveryDocumentValidation: true,
      clearHashAfterLogin: true,
      showDebugInformation: false,
      timeoutFactor: 0.75,
      sessionChecksEnabled: false
    };

    this.oauth.configure(config);
    this.oauth.setStorage(sessionStorage);

    try {
      await this.oauth.loadDiscoveryDocumentAndTryLogin();
      this.oauth.setupAutomaticSilentRefresh();
      this.authErrorSignal.set(null);
    } catch (error) {
      console.error('OIDC initialization failed', error);
      this.authErrorSignal.set('We could not reach the secure sign-in service. Please try again.');
    } finally {
      this.initializedSignal.set(true);
      this.syncState();
    }
  }

  login(returnUrl = '/feed'): void {
    sessionStorage.setItem(RETURN_URL_KEY, this.safeReturnUrl(returnUrl));
    this.oauth.initCodeFlow();
  }

  register(returnUrl = '/onboarding'): void {
    if (this.hasValidToken()) return;

    sessionStorage.setItem(RETURN_URL_KEY, this.safeReturnUrl(returnUrl));
    this.oauth.initCodeFlow('', { prompt: 'create' });
  }

  logout(): void {
    sessionStorage.removeItem(RETURN_URL_KEY);
    this.oauth.logOut();
  }

  consumeReturnUrl(): string {
    const returnUrl = this.safeReturnUrl(sessionStorage.getItem(RETURN_URL_KEY) || '/feed');
    sessionStorage.removeItem(RETURN_URL_KEY);
    return returnUrl;
  }

  accessToken(): string {
    return this.oauth.getAccessToken();
  }

  hasValidToken(): boolean {
    return this.oauth.hasValidAccessToken();
  }

  retryInitialization(): Promise<void> {
    this.initializedSignal.set(false);
    return this.initialize();
  }

  private syncState(): void {
    const authenticated = this.oauth.hasValidAccessToken();
    this.authenticatedSignal.set(authenticated);
    this.claimsSignal.set(authenticated ? ((this.oauth.getIdentityClaims() || {}) as IdentityClaims) : {});
  }

  private safeReturnUrl(value: string): string {
    return value.startsWith('/') && !value.startsWith('//') ? value : '/feed';
  }
}
