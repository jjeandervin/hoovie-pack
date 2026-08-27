import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { RuntimeConfigService } from './runtime-config.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const config = inject(RuntimeConfigService);
  const isApiRequest = request.url.startsWith(config.apiBaseUrl) || request.url.startsWith('/api');
  const token = auth.accessToken();

  const outgoing = isApiRequest && token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}`, Accept: 'application/json' } })
    : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && isApiRequest) {
        sessionStorage.setItem('hooviepack.returnUrl', window.location.pathname + window.location.search);
      }
      return throwError(() => error);
    })
  );
};
