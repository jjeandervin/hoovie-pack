import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.hasValidToken()) return true;
  if (auth.authError()) return router.createUrlTree(['/login'], { queryParams: { error: 'auth' } });

  auth.login(state.url);
  return false;
};
