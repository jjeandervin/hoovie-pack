import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Welcome · HooviePack',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent)
  },
  {
    path: 'auth/callback',
    title: 'Signing in · HooviePack',
    loadComponent: () => import('./features/auth/auth-callback.component').then((m) => m.AuthCallbackComponent)
  },
  {
    path: 'onboarding',
    canActivate: [authGuard],
    title: 'Welcome to the pack · HooviePack',
    loadComponent: () =>
      import('./features/onboarding/onboarding.component').then((m) => m.OnboardingComponent)
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/app-shell.component').then((m) => m.AppShellComponent),
    children: [
      {
        path: 'feed',
        title: 'Family feed · HooviePack',
        loadComponent: () => import('./features/feed/feed.component').then((m) => m.FeedComponent)
      },
      {
        path: 'posts/new',
        title: 'Share an update · HooviePack',
        loadComponent: () =>
          import('./features/posts/post-editor.component').then((m) => m.PostEditorComponent)
      },
      {
        path: 'posts/:postId/edit',
        title: 'Edit post · HooviePack',
        loadComponent: () =>
          import('./features/posts/post-editor.component').then((m) => m.PostEditorComponent)
      },
      {
        path: 'family',
        title: 'The pack · HooviePack',
        loadComponent: () => import('./features/family/family.component').then((m) => m.FamilyComponent)
      },
      {
        path: 'family/settings',
        title: 'Family settings · HooviePack',
        loadComponent: () =>
          import('./features/family/family-settings.component').then((m) => m.FamilySettingsComponent)
      },
      {
        path: 'members/:memberId',
        title: 'Pack member · HooviePack',
        loadComponent: () =>
          import('./features/family/member-detail.component').then((m) => m.MemberDetailComponent)
      },
      {
        path: 'dogs',
        title: 'Dogs of the family · HooviePack',
        loadComponent: () => import('./features/dogs/dogs.component').then((m) => m.DogsComponent)
      },
      {
        path: 'dogs/new',
        title: 'Add a pup · HooviePack',
        loadComponent: () => import('./features/dogs/dog-editor.component').then((m) => m.DogEditorComponent)
      },
      {
        path: 'dogs/:dogId',
        title: 'Dog profile · HooviePack',
        loadComponent: () =>
          import('./features/dogs/dog-detail.component').then((m) => m.DogDetailComponent)
      },
      {
        path: 'dogs/:dogId/edit',
        title: 'Edit dog · HooviePack',
        loadComponent: () => import('./features/dogs/dog-editor.component').then((m) => m.DogEditorComponent)
      },
      {
        path: 'profile',
        title: 'Your profile · HooviePack',
        loadComponent: () => import('./features/profile/profile.component').then((m) => m.ProfileComponent)
      },
      { path: '', pathMatch: 'full', redirectTo: 'feed' }
    ]
  },
  { path: '**', redirectTo: 'feed' }
];
