import '@angular/compiler';
import { ProfileComponent } from '../../src/app/features/profile/profile.component';
import { MemberDetailComponent } from '../../src/app/features/family/member-detail.component';
import { CalendarPageComponent } from '../../src/app/features/calendar/calendar-page.component';
import { CalendarEventFormComponent } from '../../src/app/features/calendar/calendar-event-form.component';
import { CalendarEventDetailComponent } from '../../src/app/features/calendar/calendar-event-detail.component';
import { Component } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, RouterOutlet, withInMemoryScrolling } from '@angular/router';
import { ToolsComponent } from '../../src/app/features/tools/tools.component';
import { DogipediaComponent } from '../../src/app/features/dogipedia/dogipedia.component';
import { BreedDetailComponent } from '../../src/app/features/dogipedia/breed-detail.component';
import { DogEditorComponent } from '../../src/app/features/dogs/dog-editor.component';
import { DogDetailComponent } from '../../src/app/features/dogs/dog-detail.component';
import { RuntimeConfigService } from '../../src/app/core/runtime-config.service';
import { AppShellComponent } from '../../src/app/layout/app-shell.component';
import { PostEditorComponent } from '../../src/app/features/posts/post-editor.component';
import { FeedComponent } from '../../src/app/features/feed/feed.component';
import { ActiveFamilyService } from '../../src/app/core/active-family.service';
import { CurrentUserService } from '../../src/app/core/current-user.service';
import { AuthService } from '../../src/app/core/auth.service';

// Run the production feature components with a local HTTP boundary. Authentication
// belongs to the app shell and is already covered separately by the auth tests.
@Component({ selector: 'hp-test-root', imports: [RouterOutlet], template: '<router-outlet />' })
class TestRoot {}

bootstrapApplication(TestRoot, {
  providers: [provideHttpClient(),
    { provide: RuntimeConfigService, useValue: { apiBaseUrl: '/api', mediaUrl: (source: string) => source?.startsWith('/test-photos/') ? source : null, isApiUrl: () => false } },
    { provide: ActiveFamilyService, useValue: {
      load: async () => [{ id: 'pack', name: 'The Hoovie family' }],
      families: () => [{ id: 'pack', name: 'The Hoovie family' }],
      activeId: () => 'pack', activeFamily: () => ({ id: 'pack', name: 'The Hoovie family' })
    } },
    { provide: CurrentUserService, useValue: { load: async () => ({}), profile: () => ({ id: 'jamie', displayName: 'Jamie' }) } },
    { provide: AuthService, useValue: { displayName: () => 'Jamie', logout: () => {} } },
    provideRouter([
      { path: '', component: AppShellComponent, children: [
        { path: 'profile', component: ProfileComponent, providers: [CurrentUserService] },
        { path: 'members/:memberId', component: MemberDetailComponent },
        { path: 'feed', component: FeedComponent },
        { path: 'posts/new', component: PostEditorComponent },
        { path: 'posts/:postId/edit', component: PostEditorComponent },
        { path: 'dogs/new', component: DogEditorComponent },
        { path: 'dogs/:dogId/edit', component: DogEditorComponent },
        { path: 'dogs/:dogId', component: DogDetailComponent },
        { path: 'tools/calendar/events/new', component: CalendarEventFormComponent },
        { path: 'tools/calendar/events/:eventId/edit', component: CalendarEventFormComponent },
        { path: 'tools/calendar/events/:eventId', component: CalendarEventDetailComponent },
        { path: 'tools/calendar', component: CalendarPageComponent },
        { path: 'tools', component: ToolsComponent },
        { path: 'dogipedia', component: DogipediaComponent },
        { path: 'dogipedia/breeds/:id', component: BreedDetailComponent }
      ] }
    ], withInMemoryScrolling({ scrollPositionRestoration: 'top' }))]
}).catch(console.error);
