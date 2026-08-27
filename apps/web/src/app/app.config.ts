import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import { routes } from './app.routes';
import { RuntimeConfigService } from './core/runtime-config.service';
import { AuthService } from './core/auth.service';
import { authInterceptor } from './core/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' })
    ),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideOAuthClient(),
    provideAppInitializer(async () => {
      const runtimeConfig = inject(RuntimeConfigService);
      const auth = inject(AuthService);
      await runtimeConfig.load();
      await auth.initialize();
    })
  ]
};
