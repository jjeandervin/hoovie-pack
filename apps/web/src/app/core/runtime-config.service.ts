import { Injectable, signal } from '@angular/core';
import { environment } from '../../environments/environment';
import { isUrlWithinBase } from './api-url';

export interface RuntimeConfig {
  apiBaseUrl: string;
  oidcIssuer: string;
  oidcClientId: string;
  oidcRedirectUri?: string;
  oidcPostLogoutRedirectUri?: string;
}

const DEFAULT_CONFIG: RuntimeConfig = {
  apiBaseUrl: '/api',
  oidcIssuer: 'http://localhost:8081/realms/hooviepack',
  oidcClientId: 'hooviepack-web'
};

const BUILD_CONFIG: RuntimeConfig = {
  ...DEFAULT_CONFIG,
  ...environment.runtimeConfig
};

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly settingsSignal = signal<RuntimeConfig>(BUILD_CONFIG);
  private loaded = false;

  readonly settings = this.settingsSignal.asReadonly();

  async load(): Promise<void> {
    if (this.loaded) return;

    if (!environment.runtimeConfigUrl) {
      this.settingsSignal.set(BUILD_CONFIG);
      this.loaded = true;
      return;
    }

    try {
      const response = await fetch(environment.runtimeConfigUrl, { cache: 'no-store' });
      if (!response.ok) throw new Error(`Runtime configuration returned ${response.status}`);
      const incoming = (await response.json()) as Partial<RuntimeConfig>;
      this.settingsSignal.set({ ...BUILD_CONFIG, ...incoming });
    } catch (error) {
      console.warn('Using default HooviePack runtime configuration', error);
      this.settingsSignal.set(BUILD_CONFIG);
    } finally {
      this.loaded = true;
    }
  }

  get apiBaseUrl(): string {
    return this.settingsSignal().apiBaseUrl.replace(/\/$/, '');
  }

  isApiUrl(value: string): boolean {
    return isUrlWithinBase(value, this.apiBaseUrl, window.location.origin);
  }

  mediaUrl(path?: string | null): string | null {
    if (!path) return null;
    if (/^(https?:|data:|blob:)/i.test(path)) return path;
    if (path.startsWith('/api/')) return `${this.apiBaseUrl}${path.slice('/api'.length)}`;
    return `${this.apiBaseUrl}/${path.replace(/^\//, '')}`;
  }
}
