import '@angular/compiler';
import { Component } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, RouterOutlet } from '@angular/router';
import { DogipediaComponent } from '../../src/app/features/dogipedia/dogipedia.component';
import { BreedDetailComponent } from '../../src/app/features/dogipedia/breed-detail.component';
import { RuntimeConfigService } from '../../src/app/core/runtime-config.service';
import { AppShellComponent } from '../../src/app/layout/app-shell.component';
import { ActiveFamilyService } from '../../src/app/core/active-family.service';
import { CurrentUserService } from '../../src/app/core/current-user.service';
import { AuthService } from '../../src/app/core/auth.service';

// Run the production feature components with a local HTTP boundary. Authentication
// belongs to the app shell and is already covered separately by the auth tests.
@Component({ selector: 'hp-test-root', imports: [RouterOutlet], template: '<router-outlet />' })
class TestRoot {}

bootstrapApplication(TestRoot, {
  providers: [provideHttpClient(),
    { provide: RuntimeConfigService, useValue: { apiBaseUrl: '/api', mediaUrl: () => null } },
    { provide: ActiveFamilyService, useValue: {
      load: async () => [{ id: 'pack', name: 'The Hoovie family' }],
      families: () => [{ id: 'pack', name: 'The Hoovie family' }],
      activeId: () => 'pack', activeFamily: () => ({ id: 'pack', name: 'The Hoovie family' })
    } },
    { provide: CurrentUserService, useValue: { load: async () => ({}), profile: () => ({ displayName: 'Jamie' }) } },
    { provide: AuthService, useValue: { displayName: () => 'Jamie', logout: () => {} } },
    provideRouter([
      { path: '', component: AppShellComponent, children: [
        { path: 'dogipedia', component: DogipediaComponent },
        { path: 'dogipedia/breeds/:id', component: BreedDetailComponent }
      ] }
    ])]
}).catch(console.error);
