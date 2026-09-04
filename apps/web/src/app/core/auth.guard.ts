import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { isFamilyInviteReturnUrl } from './auth-flow';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.hasValidToken()) return true;
  if (auth.authError()) return router.createUrlTree(['/login'], { queryParams: { error: 'auth' } });

  if (isFamilyInviteReturnUrl(state.url)) {
    auth.register(state.url);
  } else {
    auth.login(state.url);
  }
  return false;
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.hasValidToken() ? router.createUrlTree(['/feed']) : true;
};
